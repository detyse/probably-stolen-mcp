using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PSMcp.Contracts;

namespace PSMcp.Mod
{
    public sealed class HttpBridge : IDisposable
    {
        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            TypeNameHandling = TypeNameHandling.None,
            MissingMemberHandling = MissingMemberHandling.Error,
            MaxDepth = 32
        };

        private readonly TcpListener listener;
        private readonly object lifecycle = new object();
        private readonly HashSet<TcpClient> clients = new HashSet<TcpClient>();
        private readonly MainThreadQueue queue;
        private readonly GameSession session;
        private readonly string token;
        private readonly string authority;
        private readonly Action<string> logError;
        private readonly MainThreadQueue? viewQueue;
        private readonly Func<BridgeResponse>? captureView;
        private volatile bool stopped;
        private bool started;

        public HttpBridge(int port, string token, MainThreadQueue queue, GameSession session, Action<string> logError,
            MainThreadQueue? viewQueue = null, Func<BridgeResponse>? captureView = null)
        {
            if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (string.IsNullOrEmpty(token) || token.Length < 32) throw new ArgumentException("A bridge token of at least 32 characters is required.", nameof(token));
            this.token = token;
            this.queue = queue;
            this.session = session;
            this.logError = logError;
            this.viewQueue = viewQueue;
            this.captureView = captureView;
            authority = "127.0.0.1:" + port;
            // HttpListener uses HTTP.sys on Windows, whose initialization can fail
            // in restricted hosts before any URL is registered. Use sockets on
            // both .NET and Unity/Mono, with an explicit IPv4 loopback binding.
            listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
        }

        public void Start()
        {
            lock (lifecycle)
            {
                if (stopped) throw new ObjectDisposedException(nameof(HttpBridge));
                if (started) return;
                listener.Start(8);
                started = true;
                _ = Task.Run(AcceptLoop);
            }
        }

        private async Task AcceptLoop()
        {
            while (!stopped)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException || ex is InvalidOperationException)
                {
                    if (!stopped) logError("Bridge listener stopped: " + ex.GetType().Name);
                    break;
                }
                bool accepted;
                lock (lifecycle)
                {
                    if (stopped)
                    {
                        client.Dispose();
                        return;
                    }
                    accepted = clients.Count < 8;
                    clients.Add(client);
                }
                if (!accepted)
                {
                    // Track and finish this rejection before accepting another client,
                    // so overload responses are bounded and closed during shutdown too.
                    await Handle(client, busy: true).ConfigureAwait(false);
                    continue;
                }
                _ = Handle(client);
            }
        }

        private async Task Handle(TcpClient client, bool busy = false)
        {
            LoopbackHttpConnection? context = null;
            try
            {
                context = new LoopbackHttpConnection(client);
                if (busy)
                {
                    await Reply(context, 503, BridgeResponse.Error("bridge_busy", "The bridge has too many open connections."));
                    return;
                }
                await ReadInput(context.ReadHeaders()).ConfigureAwait(false);
                if (!string.Equals(context.Headers["Host"], authority, StringComparison.OrdinalIgnoreCase)
                    || context.Headers.ContainsKey("Origin"))
                {
                    await Reply(context, 403, BridgeResponse.Error("forbidden_origin", "Only local non-browser bridge requests are accepted."));
                    return;
                }
                if (!context.Headers.TryGetValue("Authorization", out var authorization)
                    || !string.Equals(authorization, "Bearer " + token, StringComparison.Ordinal))
                {
                    await Reply(context, 401, BridgeResponse.Error("unauthorized", "Load the current connection file written by the Mod."));
                    return;
                }
                var path = context.Path;
                if (path == "/health" && context.Method == "GET")
                {
                    await Reply(context, 200, BridgeResponse.Ok(message: "PSMcp bridge is listening. Use /state to check game readiness."));
                    return;
                }

                WorkTicket ticket;
                if (path == "/state" && context.Method == "GET")
                {
                    ticket = queue.Enqueue(() => BridgeResponse.Ok(session.Observe()));
                }
                else if (path == "/view" && context.Method == "GET")
                {
                    if (viewQueue == null || captureView == null)
                    {
                        await Reply(context, 200, BridgeResponse.Error("view_unavailable", "The current host does not provide game-frame capture."));
                        return;
                    }
                    ticket = viewQueue.Enqueue(captureView);
                }
                else if (path.StartsWith("/actions/", StringComparison.Ordinal) && context.Method == "GET")
                {
                    var requestId = path.Substring("/actions/".Length);
                    ticket = queue.Enqueue(() => session.GetActionStatus(requestId));
                }
                else if (path == "/actions" && context.Method == "POST")
                {
                    if (!context.ContentLength.HasValue
                        || !context.Headers.TryGetValue("Content-Type", out var contentType)
                        || !string.Equals(contentType.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
                    {
                        await Reply(context, 400, BridgeResponse.Error("invalid_request", "Use a JSON body with Content-Length, at most 16 KiB."));
                        return;
                    }
                    var readTask = context.ReadBody();
                    await ReadInput(readTask).ConfigureAwait(false);
                    var command = JsonConvert.DeserializeObject<ActionRequest>(await readTask.ConfigureAwait(false), JsonSettings);
                    if (command == null) throw new JsonException("Empty command.");
                    ticket = queue.Enqueue(() => session.Perform(command));
                }
                else
                {
                    await Reply(context, 404, BridgeResponse.Error("not_found", "Supported routes: GET /health, GET /state, GET /view, GET /actions/{requestId}, POST /actions."));
                    return;
                }

                if (await Task.WhenAny(ticket.Completion, Task.Delay(5000)).ConfigureAwait(false) != ticket.Completion)
                {
                    if (!ticket.CancelPending())
                    {
                        await Reply(context, 504, BridgeResponse.Error("action_outcome_unknown", "Execution may already have started. Read state, or retry the exact same requestId and arguments; do not issue a new command blindly."));
                        return;
                    }
                }
                await Reply(context, 200, await ticket.Completion.ConfigureAwait(false));
            }
            catch (HttpRequestError ex)
            {
                await TryReply(context, ex.Status, BridgeResponse.Error(ex.Code, ex.Message));
                if (ex.Status == 400) await FinishRejectedRequest(client);
            }
            catch (JsonException)
            {
                await TryReply(context, 400, BridgeResponse.Error("invalid_json", "The request must contain a valid action JSON object."));
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is TimeoutException)
            {
                // Disconnection or shutdown: never retry a possibly dispatched action.
            }
            catch (Exception ex)
            {
                logError("Bridge request failed: " + ex.GetType().Name);
                await TryReply(context, 500, BridgeResponse.Error("bridge_error", "Bridge request failed. Check the game log."));
            }
            finally
            {
                client.Dispose();
                lock (lifecycle) clients.Remove(client);
            }
        }

        private static async Task FinishRejectedRequest(TcpClient client)
        {
            // On Windows, closing with an unread request body can reset the socket and
            // discard the error response. Send FIN, then drain a bounded amount of input.
            try
            {
                client.Client.Shutdown(SocketShutdown.Send);
                var stream = client.GetStream();
                var buffer = new byte[4096];
                var deadline = Task.Delay(100);
                for (var received = 0; received < 65536;)
                {
                    var read = stream.ReadAsync(buffer, 0, buffer.Length);
                    if (await Task.WhenAny(read, deadline).ConfigureAwait(false) != read)
                    {
                        _ = read.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        return;
                    }
                    var count = await read.ConfigureAwait(false);
                    if (count == 0) return;
                    received += count;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException || ex is InvalidOperationException) { }
        }

        private static async Task ReadInput(Task task)
        {
            try { await WithIoTimeout(task).ConfigureAwait(false); }
            catch (TimeoutException) { throw new HttpRequestError(408, "request_timeout", "Request headers or body were not received in time."); }
        }

        private static async Task WithIoTimeout(Task task)
        {
            using var deadline = new CancellationTokenSource();
            try
            {
                if (await Task.WhenAny(task, Task.Delay(5000, deadline.Token)).ConfigureAwait(false) != task)
                {
                    // Closing the client in Handle's finally also cancels pending I/O
                    // on Mono versions that do not support stream cancellation tokens.
                    _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    throw new TimeoutException();
                }
                await task.ConfigureAwait(false);
            }
            finally { deadline.Cancel(); }
        }

        private static Task Reply(LoopbackHttpConnection context, int status, BridgeResponse result) => WithIoTimeout(context.Reply(status, result));

        private static async Task TryReply(LoopbackHttpConnection? context, int status, BridgeResponse result)
        {
            if (context == null) return;
            try { await Reply(context, status, result).ConfigureAwait(false); }
            catch (Exception) { /* The caller may already have disconnected. */ }
        }

        public void Dispose()
        {
            lock (lifecycle)
            {
                if (stopped) return;
                stopped = true;
                listener.Stop();
                foreach (var client in clients) client.Dispose();
                queue.Dispose();
                viewQueue?.Dispose();
            }
        }
    }
}

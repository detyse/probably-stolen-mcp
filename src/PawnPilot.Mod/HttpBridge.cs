using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PawnPilot.Contracts;

namespace PawnPilot.Mod
{
    public sealed class HttpBridge : IDisposable
    {
        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            TypeNameHandling = TypeNameHandling.None,
            MaxDepth = 32
        };

        private readonly HttpListener listener = new HttpListener();
        private readonly SemaphoreSlim clients = new SemaphoreSlim(8);
        private readonly MainThreadQueue queue;
        private readonly GameSession session;
        private readonly string token;
        private readonly string authority;
        private readonly Action<string> logError;
        private volatile bool stopped;

        public HttpBridge(int port, string token, MainThreadQueue queue, GameSession session, Action<string> logError)
        {
            if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (string.IsNullOrEmpty(token) || token.Length < 32) throw new ArgumentException("A bridge token of at least 32 characters is required.", nameof(token));
            this.token = token;
            this.queue = queue;
            this.session = session;
            this.logError = logError;
            authority = "127.0.0.1:" + port;
            listener.Prefixes.Add("http://" + authority + "/");
        }

        public void Start()
        {
            listener.Start();
            _ = Task.Run(AcceptLoop);
        }

        private async Task AcceptLoop()
        {
            while (!stopped)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException)
                {
                    if (!stopped) logError("Bridge listener stopped: " + ex.GetType().Name);
                    break;
                }
                if (!clients.Wait(0))
                {
                    context.Response.StatusCode = 503;
                    context.Response.Close();
                    continue;
                }
                _ = Handle(context);
            }
        }

        private async Task Handle(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                if (!string.Equals(request.Headers["Host"], authority, StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrEmpty(request.Headers["Origin"]))
                {
                    await Reply(context, 403, BridgeResponse.Error("forbidden_origin", "Only local non-browser bridge requests are accepted."));
                    return;
                }
                if (!string.Equals(request.Headers["Authorization"], "Bearer " + token, StringComparison.Ordinal))
                {
                    await Reply(context, 401, BridgeResponse.Error("unauthorized", "Load the current connection file written by the Mod."));
                    return;
                }
                var path = request.Url?.AbsolutePath;
                if (path == "/health" && request.HttpMethod == "GET")
                {
                    await Reply(context, 200, BridgeResponse.Ok(message: "PawnPilot bridge is listening. Use /state to check game readiness."));
                    return;
                }

                WorkTicket ticket;
                if (path == "/state" && request.HttpMethod == "GET")
                {
                    ticket = queue.Enqueue(() => BridgeResponse.Ok(session.Observe()));
                }
                else if (path == "/actions" && request.HttpMethod == "POST")
                {
                    if (request.ContentLength64 < 0 || request.ContentLength64 > 16384
                        || !(request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                    {
                        await Reply(context, 400, BridgeResponse.Error("invalid_request", "Use a JSON body with Content-Length, at most 16 KiB."));
                        return;
                    }
                    var readTask = ReadBody(request.InputStream);
                    if (await Task.WhenAny(readTask, Task.Delay(5000)).ConfigureAwait(false) != readTask)
                    {
                        request.InputStream.Close();
                        _ = readTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        await Reply(context, 408, BridgeResponse.Error("request_timeout", "Request body was not received in time."));
                        return;
                    }
                    var command = JsonConvert.DeserializeObject<ActionRequest>(await readTask.ConfigureAwait(false), JsonSettings);
                    if (command == null) throw new JsonException("Empty command.");
                    ticket = queue.Enqueue(() => session.Perform(command));
                }
                else
                {
                    await Reply(context, 404, BridgeResponse.Error("not_found", "Supported routes: GET /health, GET /state, POST /actions."));
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
            catch (JsonException)
            {
                await TryReply(context, 400, BridgeResponse.Error("invalid_json", "The request must contain a valid action JSON object."));
            }
            catch (Exception ex)
            {
                logError("Bridge request failed: " + ex.GetType().Name);
                await TryReply(context, 500, BridgeResponse.Error("bridge_error", "Bridge request failed. Check the game log."));
            }
            finally
            {
                try { context.Response.Close(); }
                catch (Exception) { /* Listener shutdown can close an in-flight response. */ }
                finally { clients.Release(); }
            }
        }

        private static async Task<string> ReadBody(Stream stream)
        {
            using var output = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > 16384) throw new JsonException("Request is too large.");
                output.Write(buffer, 0, read);
            }
            return new UTF8Encoding(false, true).GetString(output.ToArray());
        }

        private static async Task Reply(HttpListenerContext context, int status, BridgeResponse result)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result, JsonSettings));
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.KeepAlive = false;
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        private static async Task TryReply(HttpListenerContext context, int status, BridgeResponse result)
        {
            try { await Reply(context, status, result).ConfigureAwait(false); }
            catch (Exception) { /* The caller may already have disconnected. */ }
        }

        public void Dispose()
        {
            stopped = true;
            listener.Close();
            queue.Dispose();
        }
    }
}

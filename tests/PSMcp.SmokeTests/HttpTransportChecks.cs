using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using PSMcp.Contracts;
using PSMcp.Mod;
using HttpRequestError = PSMcp.Mod.HttpRequestError;

internal static class HttpTransportChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        const string host = "Host: 127.0.0.1:15527\r\n";
        var body = "{\"action\":\"测试\"}";
        var request = "POST /actions?test=1 HTTP/1.1\r\n" + host
            + "Content-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\n\r\n" + body;
        foreach (var fragmentSize in new[] { 1, 7, 8192 })
        {
            using var stream = new FragmentedStream(Encoding.UTF8.GetBytes(request), fragmentSize);
            var connection = new LoopbackHttpConnection(stream);
            await connection.ReadHeaders();
            check(connection.Method == "POST" && connection.Path == "/actions"
                && connection.Headers["host"] == "127.0.0.1:15527" && await connection.ReadBody() == body,
                $"HTTP request framing handles {fragmentSize}-byte fragments and UTF-8 bodies");
        }

        var invalidRequests = new (string Name, string Request, int Status)[]
        {
            ("missing Host", "GET /health HTTP/1.1\r\n\r\n", 400),
            ("duplicate Host", "GET /health HTTP/1.1\r\n" + host + host.ToLowerInvariant() + "\r\n", 400),
            ("duplicate Content-Length", "POST /actions HTTP/1.1\r\n" + host + "Content-Length: 2\r\ncontent-length: 3\r\n\r\n{}", 400),
            ("negative Content-Length", "POST /actions HTTP/1.1\r\n" + host + "Content-Length: -1\r\n\r\n", 400),
            ("oversized Content-Length", "POST /actions HTTP/1.1\r\n" + host + "Content-Length: 16385\r\n\r\n", 400),
            ("overflowing Content-Length", "POST /actions HTTP/1.1\r\n" + host + "Content-Length: 999999999999\r\n\r\n", 400),
            ("chunked encoding", "POST /actions HTTP/1.1\r\n" + host + "Transfer-Encoding: chunked\r\n\r\n", 400),
            ("ambiguous body framing", "POST /actions HTTP/1.1\r\n" + host + "Content-Length: 0\r\nTransfer-Encoding: chunked\r\n\r\n", 400),
            ("100-continue expectation", "POST /actions HTTP/1.1\r\n" + host + "Expect: 100-continue\r\n\r\n", 417),
            ("folded headers", "GET /health HTTP/1.1\r\n" + host + " continuation\r\n\r\n", 400),
            ("bare line feed", "GET /health HTTP/1.1\r\n" + host + "X-Test: a\nb\r\n\r\n", 400),
            ("control characters", "GET /health HTTP/1.1\r\n" + host + "X-Test: \0\r\n\r\n", 400),
            ("absolute request target", "GET http://example.com/health HTTP/1.1\r\n" + host + "\r\n", 400),
            ("truncated headers", "GET /health HTTP/1.1\r\n" + host, 400),
            ("oversized headers", "GET /health HTTP/1.1\r\n" + host + "X-Test: " + new string('x', 8192) + "\r\n\r\n", 431)
        };
        foreach (var invalid in invalidRequests)
        {
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(invalid.Request));
            var connection = new LoopbackHttpConnection(stream);
            var status = 0;
            try { await connection.ReadHeaders(); }
            catch (HttpRequestError error) { status = error.Status; }
            check(status == invalid.Status, "HTTP parser rejects " + invalid.Name);
        }

        foreach (var invalidBody in new[] { new byte[] { (byte)'{' }, new byte[] { 0xc3, 0x28 } })
        {
            var header = Encoding.ASCII.GetBytes("POST /actions HTTP/1.1\r\n" + host + "Content-Length: 2\r\n\r\n");
            using var stream = new MemoryStream(header.Concat(invalidBody).ToArray());
            var connection = new LoopbackHttpConnection(stream);
            await connection.ReadHeaders();
            var rejected = false;
            try { await connection.ReadBody(); }
            catch (HttpRequestError error) { rejected = error.Status == 400; }
            check(rejected, invalidBody.Length == 1 ? "truncated bodies are rejected" : "invalid UTF-8 is rejected");
        }

        using (var stream = new FragmentedStream(Encoding.ASCII.GetBytes("POST /actions HTTP/1.1\r\n" + host
            + "Content-Length: 16384\r\n\r\n" + new string('x', 16384)), 103))
        {
            var connection = new LoopbackHttpConnection(stream);
            await connection.ReadHeaders();
            check((await connection.ReadBody()).Length == 16384, "bodies at the 16 KiB limit are accepted");
        }

        using (var stream = new MemoryStream())
        {
            await new LoopbackHttpConnection(stream).Reply(200, BridgeResponse.Ok(message: "测试"));
            var response = Encoding.UTF8.GetString(stream.ToArray());
            var separator = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var responseBody = response[(separator + 4)..];
            check(response.StartsWith("HTTP/1.1 200 OK\r\n", StringComparison.Ordinal)
                && response.Contains("Content-Length: " + Encoding.UTF8.GetByteCount(responseBody) + "\r\n")
                && response.Contains("Connection: close\r\nCache-Control: no-store\r\n")
                && JsonConvert.DeserializeObject<BridgeResponse>(responseBody)?.Message == "测试",
                "HTTP responses frame UTF-8 JSON with a byte length and close the connection");
        }

        // Binding is independently testable even when the host blocks outbound connections.
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var queue = new MainThreadQueue();
        var session = new GameSession(new FakeGame(), s => JsonConvert.SerializeObject(s, HttpBridge.JsonSettings));
        using var server = new HttpBridge(port, new string('a', 32), queue, session, Console.Error.WriteLine);
        server.Start();
        server.Start();
        check(true, "production HTTP bridge starts without HTTP.sys and tolerates a repeated Start");
        using (var duplicate = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true })
        {
            var refused = false;
            try { duplicate.Start(); }
            catch (SocketException) { refused = true; }
            check(refused, "a second listener cannot take the bridge port");
        }
        server.Dispose();
        server.Dispose();
        using (var replacement = new TcpListener(IPAddress.Loopback, port))
        {
            replacement.Start();
            check(true, "bridge shutdown releases its port and is safe to repeat");
        }
        var disposed = false;
        try { server.Start(); }
        catch (ObjectDisposedException) { disposed = true; }
        check(disposed, "a disposed bridge cannot restart");
    }

    public static async Task RunNetwork(int port, string token, Action<bool, string> check)
    {
        var headers = $"Host: 127.0.0.1:{port}\r\nAuthorization: Bearer {token}\r\n";
        check((await Exchange(port, "GET /health HTTP/1.1\r\n" + headers + "\r\n", fragmentSize: 1)).StartsWith("HTTP/1.1 200 "),
            "fragmented TCP headers reach the production HTTP health handler");
        check((await Exchange(port, "GET /health HTTP/1.1\r\nHost: example.com\r\nAuthorization: Bearer " + token + "\r\n\r\n")).StartsWith("HTTP/1.1 403 "),
            "requests with an unexpected Host are rejected");
        check((await Exchange(port, "GET /health HTTP/1.1\r\n" + headers + "Origin:\r\n\r\n")).StartsWith("HTTP/1.1 403 "),
            "even an empty browser Origin header is rejected");
        check((await Exchange(port, "POST /actions HTTP/1.1\r\n" + headers + "Content-Type: application/json\r\nContent-Length: 2\r\n\r\n{", endInput: true)).StartsWith("HTTP/1.1 400 "),
            "a truncated TCP body never reaches the action queue");

        var timeouts = await Task.WhenAll(
            Exchange(port, "GET /health HTTP/1.1\r\n" + headers),
            Exchange(port, "POST /actions HTTP/1.1\r\n" + headers + "Content-Type: application/json\r\nContent-Length: 2\r\n\r\n{"));
        check(timeouts.All(response => response.StartsWith("HTTP/1.1 408 ")),
            "incomplete TCP headers and bodies time out and close their connections");
        check((await Exchange(port, "GET /health HTTP/1.1\r\n" + headers + "\r\n")).StartsWith("HTTP/1.1 200 "),
            "HTTP listener remains usable after malformed and timed-out requests");
    }

    private static async Task<string> Exchange(int port, string request, bool endInput = false, int fragmentSize = 8192)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
        var stream = client.GetStream();
        var bytes = Encoding.UTF8.GetBytes(request);
        for (var offset = 0; offset < bytes.Length; offset += fragmentSize)
            await stream.WriteAsync(bytes.AsMemory(offset, Math.Min(fragmentSize, bytes.Length - offset)), deadline.Token);
        if (endInput) client.Client.Shutdown(SocketShutdown.Send);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(deadline.Token);
    }

    private sealed class FragmentedStream(byte[] bytes, int maximumRead) : MemoryStream(bytes)
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => base.ReadAsync(buffer, offset, Math.Min(count, maximumRead), cancellationToken);
    }
}

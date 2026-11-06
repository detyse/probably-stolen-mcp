using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PSMcp.Contracts;

namespace PSMcp.Mod
{
    // A deliberately small HTTP/1.x transport for the local bridge routes.
    // One request per connection; no chunking, upgrades, or persistent connections.
    internal sealed class LoopbackHttpConnection
    {
        private const int MaximumHeaders = 8192;
        internal const int MaximumBody = 16384;
        private readonly Stream stream;
        private readonly byte[] buffer = new byte[MaximumHeaders];
        private int bodyOffset;
        private int bufferedBodyLength;

        public string Method { get; private set; } = "";
        public string Path { get; private set; } = "";
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int? ContentLength { get; private set; }

        public LoopbackHttpConnection(TcpClient client) : this(client.GetStream()) { }
        internal LoopbackHttpConnection(Stream stream) => this.stream = stream;

        public async Task ReadHeaders()
        {
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer, count, buffer.Length - count).ConfigureAwait(false);
                if (read == 0) throw InvalidRequest("Incomplete HTTP headers.");
                var previous = count;
                count += read;
                for (var i = Math.Max(0, previous - 3); i <= count - 4; i++)
                {
                    if (buffer[i] != 13 || buffer[i + 1] != 10 || buffer[i + 2] != 13 || buffer[i + 3] != 10) continue;
                    bodyOffset = i + 4;
                    bufferedBodyLength = count - bodyOffset;
                    ParseHeaders(i);
                    return;
                }
            }
            throw new HttpRequestError(431, "invalid_request", "HTTP headers exceed 8 KiB.");
        }

        private void ParseHeaders(int length)
        {
            for (var i = 0; i < length; i++)
            {
                var value = buffer[i];
                if (value > 126 || (value < 32 && value != 9 && value != 13 && value != 10))
                    throw InvalidRequest("HTTP headers must use ASCII text.");
            }
            var lines = Encoding.ASCII.GetString(buffer, 0, length).Split(new[] { "\r\n" }, StringSplitOptions.None);
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length != 3 || !IsToken(requestLine[0])
                || !requestLine[1].StartsWith("/", StringComparison.Ordinal)
                || requestLine[1].IndexOfAny(new[] { '\r', '\n', '\t', '#' }) >= 0
                || (requestLine[2] != "HTTP/1.1" && requestLine[2] != "HTTP/1.0"))
                throw InvalidRequest("Use an HTTP/1.x request with a local path.");
            Method = requestLine[0];
            Path = requestLine[1].Split('?')[0];
            for (var i = 1; i < lines.Length; i++)
            {
                var colon = lines[i].IndexOf(':');
                if (colon <= 0 || !IsToken(lines[i].Substring(0, colon)))
                    throw InvalidRequest("Malformed HTTP header.");
                var name = lines[i].Substring(0, colon);
                var value = lines[i].Substring(colon + 1).Trim(' ', '\t');
                if (value.IndexOfAny(new[] { '\r', '\n' }) >= 0 || Headers.ContainsKey(name))
                    throw InvalidRequest("Duplicate or malformed HTTP header.");
                Headers.Add(name, value);
            }
            if (!Headers.ContainsKey("Host")) throw InvalidRequest("Host is required.");
            if (Headers.ContainsKey("Transfer-Encoding"))
                throw InvalidRequest("Transfer-Encoding is unsupported. Use Content-Length.");
            if (Headers.ContainsKey("Expect"))
                throw new HttpRequestError(417, "invalid_request", "Expect is unsupported. Send the request body directly.");
            if (Headers.TryGetValue("Content-Length", out var text))
            {
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size > MaximumBody)
                    throw InvalidRequest("Use a Content-Length of at most 16 KiB.");
                ContentLength = size;
            }
        }

        private static bool IsToken(string value)
        {
            if (value.Length == 0) return false;
            foreach (var c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')
                    && "!#$%&'*+-.^_`|~".IndexOf(c) < 0) return false;
            return true;
        }

        public async Task<string> ReadBody()
        {
            var body = new byte[ContentLength ?? 0];
            var count = Math.Min(bufferedBodyLength, body.Length);
            Buffer.BlockCopy(buffer, bodyOffset, body, 0, count);
            while (count < body.Length)
            {
                var read = await stream.ReadAsync(body, count, body.Length - count).ConfigureAwait(false);
                if (read == 0) throw InvalidRequest("Incomplete HTTP body.");
                count += read;
            }
            try { return new UTF8Encoding(false, true).GetString(body); }
            catch (DecoderFallbackException) { throw InvalidRequest("The JSON body must be valid UTF-8."); }
        }

        public async Task Reply(int status, BridgeResponse result)
        {
            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result, HttpBridge.JsonSettings));
            var reason = status switch
            {
                200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden",
                404 => "Not Found", 408 => "Request Timeout", 417 => "Expectation Failed",
                431 => "Request Header Fields Too Large", 503 => "Service Unavailable",
                504 => "Gateway Timeout", _ => "Internal Server Error"
            };
            var headers = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + reason + "\r\n"
                + "Content-Type: application/json; charset=utf-8\r\n"
                + "Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n"
                + "Connection: close\r\nCache-Control: no-store\r\n\r\n");
            await stream.WriteAsync(headers, 0, headers.Length).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
        }

        private static HttpRequestError InvalidRequest(string message) => new HttpRequestError(400, "invalid_request", message);
    }

    internal sealed class HttpRequestError : Exception
    {
        public int Status { get; }
        public string Code { get; }
        public HttpRequestError(int status, string code, string message) : base(message)
        {
            Status = status;
            Code = code;
        }
    }
}

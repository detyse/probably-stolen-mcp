using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PSMcp.Contracts;

namespace PSMcp.Server;

public sealed class BridgeClient : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string connectionFile;
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(8),
        MaxResponseContentBufferSize = 4 * 1024 * 1024
    };

    public BridgeClient(string connectionFile) => this.connectionFile = Path.GetFullPath(connectionFile);

    public Task<BridgeResponse> Health(CancellationToken cancellation = default) => Send("health", null, cancellation);
    public Task<BridgeResponse> State(CancellationToken cancellation = default) => Send("state", null, cancellation);
    public Task<BridgeResponse> View(CancellationToken cancellation = default) => Send("view", null, cancellation);
    public Task<BridgeResponse> ActionStatus(string requestId, CancellationToken cancellation = default) => Send("actions/" + Uri.EscapeDataString(requestId), null, cancellation);
    public Task<BridgeResponse> Perform(ActionRequest action, CancellationToken cancellation = default) => Send("actions", action, cancellation);

    private async Task<BridgeResponse> Send(string path, ActionRequest? action, CancellationToken cancellation)
    {
        ConnectionInfo? connection;
        try
        {
            // Re-read each call: the Mod rotates credentials on every game restart.
            if (new FileInfo(connectionFile).Length > 16384) throw new InvalidDataException();
            connection = JsonSerializer.Deserialize<ConnectionInfo>(await File.ReadAllTextAsync(connectionFile, cancellation), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return BridgeResponse.Error("connection_file_unavailable", "Enable Probably Automated in the game's Mods menu and restart the game. Set --connection or PSMCP_CONNECTION_FILE to the connection.json path shown in the Mod log.");
        }

        if (connection?.ProtocolVersion != Protocol.Version)
            return BridgeResponse.Error("protocol_mismatch", "The Mod and MCP server use different protocol versions. Update them together.");
        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != "http" || baseUri.Host != "127.0.0.1" || baseUri.Port < 1024
            || baseUri.AbsolutePath != "/" || baseUri.UserInfo.Length != 0 || baseUri.Query.Length != 0 || baseUri.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(connection.Token) || connection.Token.Length < 32 || connection.Token.Length > 256
            || connection.Token.Any(char.IsWhiteSpace))
            return BridgeResponse.Error("invalid_connection", "The connection file must specify a loopback HTTP URL and a valid bridge token.");

        try
        {
            using var request = new HttpRequestMessage(action == null ? HttpMethod.Get : HttpMethod.Post, new Uri(baseUri, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
            if (action != null)
                request.Content = new StringContent(JsonSerializer.Serialize(action, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, cancellation);
            var text = await response.Content.ReadAsStringAsync(cancellation);
            var result = JsonSerializer.Deserialize<BridgeResponse>(text, JsonOptions);
            if (result == null || result.ProtocolVersion != Protocol.Version)
                return BridgeResponse.Error("protocol_mismatch", "The bridge returned an incompatible response.");
            if (!response.IsSuccessStatusCode && result.Success)
                return BridgeResponse.Error("bridge_http_error", "The bridge returned an unsuccessful HTTP status.");
            return result;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return BridgeResponse.Error(action == null ? "bridge_timeout" : "action_outcome_unknown",
                action == null ? "The game bridge timed out. Check whether the game is responsive."
                    : "The response timed out; the action may have executed. Read state, or retry the exact same requestId and arguments.", action?.RequestId);
        }
        catch (HttpRequestException)
        {
            return BridgeResponse.Error(action == null ? "bridge_unavailable" : "action_outcome_unknown",
                action == null ? "Cannot reach the Mod. Start the game with Probably Automated enabled."
                    : "The connection failed; execution may have started. Read state before issuing a new action.", action?.RequestId);
        }
        catch (JsonException)
        {
            return BridgeResponse.Error("invalid_bridge_response", "The bridge did not return the expected JSON response. Check that Mod and MCP versions match.");
        }
    }

    public void Dispose() => http.Dispose();
}

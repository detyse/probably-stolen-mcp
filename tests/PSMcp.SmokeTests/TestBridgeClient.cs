using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PSMcp.Contracts;

// Test-only HTTP harness. Production client validation is exercised in tests/python.
internal sealed class TestBridgeClient : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string connectionFile;
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
    public TestBridgeClient(string connectionFile) => this.connectionFile = connectionFile;
    public Task<BridgeResponse> Health() => Send("health");
    public Task<BridgeResponse> State() => Send("state");
    public Task<BridgeResponse> Perform(ActionRequest action) => Send("actions", action);
    private async Task<BridgeResponse> Send(string route, ActionRequest? action = null)
    {
        var connection = JsonSerializer.Deserialize<ConnectionInfo>(await File.ReadAllTextAsync(connectionFile), JsonOptions)!;
        using var request = new HttpRequestMessage(action == null ? HttpMethod.Get : HttpMethod.Post, connection.BaseUrl + route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
        if (action != null) request.Content = new StringContent(JsonSerializer.Serialize(action, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<BridgeResponse>(await response.Content.ReadAsStringAsync(), JsonOptions)!;
    }
    public void Dispose() => http.Dispose();
}

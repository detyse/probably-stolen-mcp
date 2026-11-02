using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using PSMcp.Contracts;
using PSMcp.Server;
using PSMcp.Mod;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

GameSession Session(FakeGame game) => new(game, s => JsonConvert.SerializeObject(s, HttpBridge.JsonSettings));
ActionRequest Command(GameSnapshot state, string action = "next_customer") => new()
{
    RequestId = Guid.NewGuid().ToString(), ExpectedSnapshotId = state.SnapshotId, Action = action
};

var fake = new FakeGame();
var session = Session(fake);
var initial = session.Observe();
Check(initial.SnapshotId == session.Observe().SnapshotId, "unchanged observations retain their revision");
var command = Command(initial);
Check(session.Perform(command).Success && fake.Actions == 1, "available action executes once");
Check(session.Perform(command).Success && fake.Actions == 1, "identical retry is deduplicated");
Check(session.Perform(new ActionRequest { RequestId = command.RequestId, ExpectedSnapshotId = initial.SnapshotId, Action = "open_shop" }).ErrorCode == "request_id_conflict", "conflicting retry is rejected");
Check(session.Perform(Command(initial)).ErrorCode == "stale_snapshot" && fake.Actions == 1, "old observation cannot trigger another action");
var changed = session.Observe();
fake.Cash++;
Check(session.Perform(Command(changed)).ErrorCode == "stale_snapshot", "player-side changes invalidate the snapshot");
Check(session.Perform(Command(session.Observe(), "give_money")).ErrorCode == "action_unavailable", "unlisted actions cannot execute");
var preReload = session.Observe();
session.Reset();
Check(session.Perform(Command(preReload)).ErrorCode == "stale_snapshot", "save/scene transition invalidates old commands");
fake.GameLoaded = false;
Check(session.Perform(Command(session.Observe())).ErrorCode == "action_unavailable", "actions cannot execute at the main menu");
fake.GameLoaded = true;
fake.ThrowAfterAction = true;
var partial = Command(session.Observe());
Check(session.Perform(partial).ErrorCode == "action_outcome_unknown", "partially failed actions report uncertainty");
var countAfterFailure = fake.Actions;
session.Perform(partial);
Check(fake.Actions == countAfterFailure, "retry after a partial failure does not repeat the effect");

using (var queue = new MainThreadQueue())
{
    var effects = 0;
    var expired = queue.Enqueue(() => { effects++; return BridgeResponse.Ok(); });
    Check(expired.CancelPending(), "pending work can expire");
    queue.Drain();
    Check(effects == 0 && (await expired.Completion).ErrorCode == "queue_timeout", "expired work never executes on a later frame");
    for (var i = 0; i < 64; i++) queue.Enqueue(() => BridgeResponse.Ok());
    Check((await queue.Enqueue(() => BridgeResponse.Ok()).Completion).ErrorCode == "bridge_busy", "queue capacity is bounded");
    queue.Dispose();
    Check((await queue.Enqueue(() => BridgeResponse.Ok()).Completion).ErrorCode == "bridge_stopped", "shutdown refuses new work");
}

var outputRoot = Path.Combine(Path.GetTempPath(), "probably-stolen-mcp-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(outputRoot);
var connectionFile = Path.Combine(outputRoot, "connection.json");
using var probe = new TcpListener(IPAddress.Loopback, 0);
probe.Start();
var port = ((IPEndPoint)probe.LocalEndpoint).Port;
probe.Stop();
var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
var connection = new ConnectionInfo { BaseUrl = $"http://127.0.0.1:{port}/", Token = token };
await File.WriteAllTextAsync(connectionFile, System.Text.Json.JsonSerializer.Serialize(connection, BridgeClient.JsonOptions));
using var networkQueue = new MainThreadQueue();
var networkGame = new FakeGame();
using var server = new HttpBridge(port, token, networkQueue, Session(networkGame), Console.Error.WriteLine);
using var stopping = new CancellationTokenSource();
using var ready = new ManualResetEventSlim();
var gameThread = new Thread(() =>
{
    networkGame.OwnerThread = Environment.CurrentManagedThreadId;
    ready.Set();
    while (!stopping.IsCancellationRequested)
    {
        networkQueue.Drain();
        Thread.Sleep(5);
    }
}) { IsBackground = true, Name = "Probably Stolen MCP fake game main thread" };
gameThread.Start();
ready.Wait();
try
{
    server.Start();
    using var bridge = new BridgeClient(connectionFile);
    Check((await bridge.Health()).Success, "authenticated HTTP bridge health check");
    var networkState = await bridge.State();
    Check(networkState.Success && networkState.State?.Cash == 500, "HTTP state capture executes on the game thread");
    var networkAction = Command(networkState.State!);
    var results = await Task.WhenAll(bridge.Perform(networkAction), bridge.Perform(networkAction));
    Check(results.All(r => r.Success) && networkGame.Actions == 1, "concurrent HTTP retries execute only once");

    using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
    Check((await http.GetAsync(connection.BaseUrl + "state")).StatusCode == HttpStatusCode.Unauthorized, "unauthenticated local requests are rejected");
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var originRequest = new HttpRequestMessage(HttpMethod.Get, connection.BaseUrl + "state");
    originRequest.Headers.Add("Origin", "https://example.com");
    Check((await http.SendAsync(originRequest)).StatusCode == HttpStatusCode.Forbidden, "browser-origin requests are rejected");
    using var invalidBody = new StringContent("{bad", Encoding.UTF8, "application/json");
    Check((await http.PostAsync(connection.BaseUrl + "actions", invalidBody)).StatusCode == HttpStatusCode.BadRequest, "malformed action JSON is rejected");
    using var oversizedBody = new StringContent(new string('x', 17000), Encoding.UTF8, "application/json");
    Check((await http.PostAsync(connection.BaseUrl + "actions", oversizedBody)).StatusCode == HttpStatusCode.BadRequest, "oversized bodies are rejected");

    var badFile = Path.Combine(outputRoot, "invalid.json");
    await File.WriteAllTextAsync(badFile, System.Text.Json.JsonSerializer.Serialize(new ConnectionInfo { BaseUrl = "http://example.com:1234/", Token = token }, BridgeClient.JsonOptions));
    using var badClient = new BridgeClient(badFile);
    Check((await badClient.Health()).ErrorCode == "invalid_connection", "MCP cannot send the token to a remote host");

    var mcpDll = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "PSMcp.Server.dll");
    var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var transport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "Probably Stolen MCP integration test",
        Command = dotnet,
        Arguments = [mcpDll, "--connection", connectionFile]
    });
    await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
    var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
    Check(tools.Select(t => t.Name).Order().SequenceEqual(new[] { "get_available_actions", "get_bridge_status", "get_game_state", "get_inventory", "perform_action" }), "real stdio MCP handshake exposes the five tools");
    var mcpState = await client.CallToolAsync("get_game_state", cancellationToken: timeout.Token);
    Check(mcpState.IsError != true && mcpState.StructuredContent.HasValue, "MCP returns structured game state");
    var state = mcpState.StructuredContent!.Value.GetProperty("state");
    var mcpArgs = new Dictionary<string, object?>
    {
        ["action"] = "next_customer",
        ["expectedSnapshotId"] = state.GetProperty("snapshotId").GetString(),
        ["requestId"] = Guid.NewGuid().ToString()
    };
    var actionResult = await client.CallToolAsync("perform_action", mcpArgs, cancellationToken: timeout.Token);
    Check(actionResult.IsError != true && networkGame.Actions == 2, "agent tool call reaches the game-thread action handler");
    await client.CallToolAsync("perform_action", mcpArgs, cancellationToken: timeout.Token);
    Check(networkGame.Actions == 2, "MCP retry preserves action deduplication");
    mcpArgs["requestId"] = Guid.NewGuid().ToString();
    var stale = await client.CallToolAsync("perform_action", mcpArgs, cancellationToken: timeout.Token);
    Check(stale.IsError == true && stale.StructuredContent!.Value.GetProperty("errorCode").GetString() == "stale_snapshot", "MCP errors remain understandable to the agent");

    File.Delete(connectionFile);
    var unavailable = await client.CallToolAsync("get_bridge_status", cancellationToken: timeout.Token);
    Check(unavailable.IsError == true && unavailable.StructuredContent!.Value.GetProperty("errorCode").GetString() == "connection_file_unavailable", "missing game connection is a tool error, not an MCP process crash");
}
finally
{
    stopping.Cancel();
    gameThread.Join(TimeSpan.FromSeconds(2));
    // Only remove files created by this test, never recursively delete user paths.
    foreach (var name in new[] { "connection.json", "invalid.json" }) File.Delete(Path.Combine(outputRoot, name));
    Directory.Delete(outputRoot);
}
Console.WriteLine($"All {passed} checks passed. Real-game loading and action behavior still require in-game validation.");

sealed class FakeGame : IGameAdapter
{
    public int Cash = 500;
    public int Actions;
    public bool GameLoaded = true;
    public bool ThrowAfterAction;
    public int? OwnerThread;

    private void AssertThread()
    {
        if (OwnerThread.HasValue && Environment.CurrentManagedThreadId != OwnerThread)
            throw new InvalidOperationException("Game access on the wrong thread.");
    }

    public GameSnapshot Capture()
    {
        AssertThread();
        return new GameSnapshot
        {
            GameLoaded = GameLoaded, Phase = GameLoaded ? "open" : "main_menu", Cash = Cash,
            Customer = new CustomerInfo { VisitId = Actions.ToString(), DisplayName = "Test customer" },
            Inventory = [new InventoryItem { Id = 42, DisplayName = "Test item", Count = 1 }],
            AvailableActions = GameLoaded ? [new AvailableAction { Name = "next_customer", Description = "Test action" }] : []
        };
    }

    public void Execute(string action)
    {
        AssertThread();
        Actions++;
        if (ThrowAfterAction) throw new InvalidOperationException("Simulated failure after mutation.");
    }
}

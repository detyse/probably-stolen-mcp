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

Exception? reportedError = null;
using (var queue = new MainThreadQueue(error => reportedError = error))
{
    var failure = new NullReferenceException("private diagnostic detail");
    var ticket = queue.Enqueue(() => throw failure);
    queue.Drain();
    var result = await ticket.Completion;
    Check(ReferenceEquals(reportedError, failure) && result.ErrorCode == "game_error"
        && !result.Message.Contains("private diagnostic detail"), "capture failures reach the game log without exposing exception details through MCP");
}
using (var queue = new MainThreadQueue(_ => throw new IOException("logger failed")))
{
    var ticket = queue.Enqueue(() => throw new InvalidOperationException());
    queue.Drain();
    Check((await ticket.Completion).ErrorCode == "game_error", "a failed logger cannot strand a game request");
}

ObservationChecks.Run(Check);
CapabilityChecks.Run(Check);
await HttpTransportChecks.Run(Check);
if (args.Length == 1 && args[0] == "--offline")
{
    Console.WriteLine($"All {passed} offline checks passed. HTTP connections and MCP end-to-end checks were NOT run; run scripts/Test.ps1 without -Offline for the full suite.");
    return;
}

var outputRoot = Path.Combine(Path.GetTempPath(), "PSMcp-tests-" + Guid.NewGuid().ToString("N"));
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
var networkGame = new FakeGame { Trade = ObservationChecks.SampleTrade() };
var networkSession = Session(networkGame);
using var server = new HttpBridge(port, token, networkQueue, networkSession, Console.Error.WriteLine, networkQueue, () =>
{
    var response = BridgeResponse.Ok(networkSession.Observe());
    response.View = new GameView { SnapshotId = response.State!.SnapshotId, Width = 1, Height = 1, MimeType = "image/png",
        ImageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5XcAAAAASUVORK5CYII=" };
    return response;
});
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
}) { IsBackground = true, Name = "PSMcp fake game main thread" };
gameThread.Start();
ready.Wait();
try
{
    server.Start();
    using (var connectivity = new TcpClient(AddressFamily.InterNetwork))
    {
        try { await connectivity.ConnectAsync(IPAddress.Loopback, port); }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            throw new InvalidOperationException("The bridge started, but this host denied a TCP connection to 127.0.0.1 (SocketError.AccessDenied). HTTP/MCP end-to-end tests cannot run in this environment. Run scripts/Test.ps1 in a normal PowerShell session; -Offline only runs session, framing, and listener lifecycle checks.", ex);
        }
    }
    using var bridge = new BridgeClient(connectionFile);
    var health = await bridge.Health();
    Check(health.Success, "authenticated HTTP bridge health check" + (health.Success ? "" : $" ({health.ErrorCode}: {health.Message})"));
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
    for (var attempt = 0; attempt < 3; attempt++)
    {
        using var oversizedBody = new StringContent(new string('x', 17000), Encoding.UTF8, "application/json");
        Check((await http.PostAsync(connection.BaseUrl + "actions", oversizedBody)).StatusCode == HttpStatusCode.BadRequest, "oversized bodies receive a complete rejection response");
    }

    await HttpTransportChecks.RunNetwork(port, token, Check);

    var badFile = Path.Combine(outputRoot, "invalid.json");
    await File.WriteAllTextAsync(badFile, System.Text.Json.JsonSerializer.Serialize(new ConnectionInfo { BaseUrl = "http://example.com:1234/", Token = token }, BridgeClient.JsonOptions));
    using var badClient = new BridgeClient(badFile);
    Check((await badClient.Health()).ErrorCode == "invalid_connection", "MCP cannot send the token to a remote host");

    var mcpDll = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "PSMcp.Server.dll");
    var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var transport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "PSMcp integration test",
        Command = dotnet,
        Arguments = [mcpDll, "--connection", connectionFile]
    });
    await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
    var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
    Check(tools.Select(t => t.Name).Order().SequenceEqual(new[] { "get_action_status", "get_available_actions", "get_bridge_status", "get_entity", "get_game_state", "get_inventory", "get_trade_state", "get_ui_state", "get_view", "perform_action" }), "real stdio MCP handshake exposes all ten tools");
    var mcpState = await client.CallToolAsync("get_game_state", cancellationToken: timeout.Token);
    Check(mcpState.IsError != true && mcpState.StructuredContent.HasValue, "MCP returns structured game state");
    var state = mcpState.StructuredContent!.Value.GetProperty("state");
    var tradeResult = await client.CallToolAsync("get_trade_state", cancellationToken: timeout.Token);
    Check(tradeResult.IsError != true
        && tradeResult.StructuredContent!.Value.GetProperty("trade").GetProperty("quotes")[0].GetProperty("amount").GetInt64() == 5_000_000_000L
        && tradeResult.StructuredContent.Value.GetProperty("snapshotId").GetString() == state.GetProperty("snapshotId").GetString(),
        "focused MCP trade tool returns the current quote with the same snapshot ID");
    var actionsResult = await client.CallToolAsync("get_available_actions", cancellationToken: timeout.Token);
    Check(actionsResult.IsError != true && actionsResult.StructuredContent!.Value.GetProperty("interaction").GetProperty("kind").GetString() == "ready",
        "available-action responses include the interaction wait state");
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

    var parameterState = await bridge.State();
    var parameterId = Guid.NewGuid().ToString();
    var parameterArgs = new Dictionary<string, object?> { ["action"] = "set_value", ["expectedSnapshotId"] = parameterState.State!.SnapshotId,
        ["requestId"] = parameterId, ["arguments"] = new Dictionary<string, object?> { ["value"] = 4 } };
    var parameterResult = await client.CallToolAsync("perform_action", parameterArgs, cancellationToken: timeout.Token);
    Check(parameterResult.IsError != true && networkGame.ParameterValue == 4, "structured MCP arguments survive stdio and HTTP deserialization");
    var unknownArgs = new Dictionary<string, object?>(parameterArgs) { ["arguments"] = new Dictionary<string, object?> { ["unknown"] = 4 } };
    var unknownResult = await client.CallToolAsync("perform_action", unknownArgs, cancellationToken: timeout.Token);
    Check(unknownResult.IsError == true && networkGame.Actions == 3, "MCP rejects unknown argument members without reaching the game");
    await client.CallToolAsync("perform_action", parameterArgs, cancellationToken: timeout.Token);
    Check(networkGame.Actions == 3, "structured MCP retry remains exactly once");
    var statusResult = await client.CallToolAsync("get_action_status", new Dictionary<string, object?> { ["requestId"] = parameterId }, cancellationToken: timeout.Token);
    Check(statusResult.IsError != true && statusResult.StructuredContent!.Value.GetProperty("actionStatus").GetString() == "dispatched" && networkGame.Actions == 3,
        "MCP action status returns the retained dispatch without replay");
    var uiResult = await client.CallToolAsync("get_ui_state", cancellationToken: timeout.Token);
    Check(uiResult.IsError != true && uiResult.StructuredContent!.Value.GetProperty("controls")[0].GetProperty("value").GetDouble() == 4, "UI observation carries current structured control values");
    var uiControl = uiResult.StructuredContent!.Value.GetProperty("controls")[0];
    Check(uiControl.GetProperty("nearbyText").GetString() == "Day 1 | 700 credits"
        && uiControl.GetProperty("bounds").GetProperty("y").GetDouble() == 0.25,
        "duplicate button context and screenshot coordinates survive real MCP transport");
    var entityResult = await client.CallToolAsync("get_entity", new Dictionary<string, object?> { ["entityId"] = "item-42" }, cancellationToken: timeout.Token);
    Check(entityResult.IsError != true && entityResult.StructuredContent!.Value.GetProperty("entity").GetProperty("count").GetInt32() == 1, "entity lookup only returns exposed items");
    var hiddenResult = await client.CallToolAsync("get_entity", new Dictionary<string, object?> { ["entityId"] = "hidden-item" }, cancellationToken: timeout.Token);
    Check(hiddenResult.IsError == true, "unexposed entities cannot be queried through MCP");
    var inventoryResult = await client.CallToolAsync("get_inventory", new Dictionary<string, object?> { ["containerId"] = "container-1", ["offset"] = 1, ["limit"] = 10 }, cancellationToken: timeout.Token);
    Check(inventoryResult.IsError != true && inventoryResult.StructuredContent!.Value.GetProperty("total").GetInt32() == 1
        && inventoryResult.StructuredContent.Value.GetProperty("inventory").GetArrayLength() == 0, "inventory pagination keeps container totals without exporting extra items");
    var viewResult = await client.CallToolAsync("get_view", cancellationToken: timeout.Token);
    Check(viewResult.IsError != true && viewResult.Content.OfType<ImageContentBlock>().Single().DecodedData.Length > 0
        && !viewResult.Content.OfType<TextContentBlock>().Single().Text.Contains("iVBOR"), "view returns an MCP image without duplicating base64 in text");
    using var unknownBody = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { action = "set_value", requestId = Guid.NewGuid().ToString(),
        expectedSnapshotId = parameterState.State.SnapshotId, arguments = new { unexpected = "ignored?" } }), Encoding.UTF8, "application/json");
    Check((await http.PostAsync(connection.BaseUrl + "actions", unknownBody)).StatusCode == HttpStatusCode.BadRequest, "HTTP rejects unknown argument members instead of silently dropping them");

    File.Delete(connectionFile);
    var unavailable = await client.CallToolAsync("get_bridge_status", cancellationToken: timeout.Token);
    Check(unavailable.IsError == true && unavailable.StructuredContent!.Value.GetProperty("errorCode").GetString() == "connection_file_unavailable", "missing game connection is a tool error, not an MCP process crash");

    using var pendingClient = new TcpClient(AddressFamily.InterNetwork);
    await pendingClient.ConnectAsync(IPAddress.Loopback, port);
    var pendingStream = pendingClient.GetStream();
    await pendingStream.WriteAsync(Encoding.ASCII.GetBytes("GET /health HTTP/1.1\r\n"));
    server.Dispose();
    var closed = false;
    try { closed = await pendingStream.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2)) == 0; }
    catch (IOException) { closed = true; }
    Check(closed, "bridge shutdown closes incomplete client connections");
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
    public double ParameterValue;
    public int? OwnerThread;
    public TradeInfo? Trade;
    public InteractionInfo Interaction = new() { Kind = "ready", WaitingFor = "player", McpSupported = true };

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
            Trade = GameLoaded ? Trade : null,
            Interaction = GameLoaded ? Interaction : ObservationRules.Describe(new InteractionSignals(), false),
            Inventory = [new InventoryItem { EntityId = "item-42", ContainerId = "container-1", Id = 42, DisplayName = "Test item", Count = 1 }],
            Containers = [new InventoryContainer { EntityId = "container-1", Items = [new InventoryItem { EntityId = "item-42", Count = 1 }] }],
            Controls = [new UiControl { EntityId = "control-1", Value = ParameterValue,
                NearbyText = "Day 1 | 700 credits", Bounds = new ScreenBounds { X = 0.5, Y = 0.25, Width = 0.1, Height = 0.03 } }],
            AvailableActions = GameLoaded ? [new AvailableAction { Name = "next_customer", Description = "Test action" },
                new AvailableAction { Name = "set_value", Parameters = [new ActionParameter { Name = "value", Type = "number", Min = 0, Max = 10, Required = true }] }] : []
        };
    }

    public Action Prepare(string action, ActionArguments arguments) => () =>
    {
        if (action == "set_value") ParameterValue = arguments.Value!.Value;
        Execute(action);
    };

    private void Execute(string action)
    {
        AssertThread();
        Actions++;
        if (ThrowAfterAction) throw new InvalidOperationException("Simulated failure after mutation.");
    }
}

using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PSMcp.Contracts;

namespace PSMcp.Server;

[McpServerToolType]
public sealed class GameTools(BridgeClient bridge)
{
    [McpServerTool(Name = "get_bridge_status", ReadOnly = true, OpenWorld = false)]
    [Description("Check whether the authenticated Probably Stolen MCP game bridge is reachable. This does not require a loaded save. Use get_game_state to inspect readiness.")]
    public async Task<CallToolResult> GetBridgeStatus(CancellationToken cancellationToken) => Reply(await bridge.Health(cancellationToken));

    [McpServerTool(Name = "get_game_state", ReadOnly = true, OpenWorld = false)]
    [Description("Read a main-thread snapshot of Probably Stolen: shop phase, cash, rent, current customer, recent dialogue, visible choices, main inventory and available actions. Use its snapshotId for the next action. Unknown/hidden properties are omitted; limitations lists unfinished features. Re-read after actions or animations.")]
    public async Task<CallToolResult> GetGameState(CancellationToken cancellationToken) => Reply(await bridge.State(cancellationToken));

    [McpServerTool(Name = "get_inventory", ReadOnly = true, OpenWorld = false)]
    [Description("Read top-level main-inventory item IDs, visible names and counts. This does not inspect nested containers, counter stock, hidden authenticity or true values.")]
    public async Task<CallToolResult> GetInventory(CancellationToken cancellationToken)
    {
        var result = await bridge.State(cancellationToken);
        if (!result.Success || result.State == null) return Reply(result);
        return Content(new { result.ProtocolVersion, result.State.SnapshotId, result.State.GameLoaded, result.State.InventoryScope, result.State.Inventory });
    }

    [McpServerTool(Name = "get_available_actions", ReadOnly = true, OpenWorld = false)]
    [Description("List currently available semantic actions and visible dialogue choices, with the snapshotId needed for perform_action. Only listed actions may be used. An empty list may mean dialogue/animation is pending or that this milestone has not implemented the required interaction.")]
    public async Task<CallToolResult> GetAvailableActions(CancellationToken cancellationToken)
    {
        var result = await bridge.State(cancellationToken);
        if (!result.Success || result.State == null) return Reply(result);
        return Content(new { result.ProtocolVersion, result.State.SnapshotId, result.State.GameLoaded, result.State.AvailableActions, result.State.DialogueChoices });
    }

    [McpServerTool(Name = "perform_action", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Dispatch one action listed in availableActions. Requires a fresh snapshotId and a caller-generated UUID requestId. Use the identical UUID AND arguments for retries after uncertain outcomes; never blindly retry with a new UUID. On stale_snapshot, inspect the returned state and make a new decision. Success means dispatched; animations and dialogue may still be running. Actions affect the current run and may be saved by the game.")]
    public async Task<CallToolResult> PerformAction(
        [Description("Exact name from availableActions, for example next_customer or dialogue_choice:0.")] string action,
        [Description("snapshotId from the observation on which this decision is based.")] string expectedSnapshotId,
        [Description("New UUID for a new decision; reuse it with unchanged arguments only for retrying that same command.")] string requestId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(requestId, "D", out _) || string.IsNullOrWhiteSpace(action)
            || action.Length > 128 || string.IsNullOrWhiteSpace(expectedSnapshotId) || expectedSnapshotId.Length > 128)
            return Reply(BridgeResponse.Error("invalid_request", "Provide a UUID requestId, a nonempty action and a nonempty snapshotId (at most 128 characters each)."));
        return Reply(await bridge.Perform(new ActionRequest { Action = action, ExpectedSnapshotId = expectedSnapshotId, RequestId = requestId }, cancellationToken));
    }

    private static CallToolResult Reply(BridgeResponse result) => Content(result, !result.Success);

    private static CallToolResult Content<T>(T value, bool isError = false)
    {
        var json = JsonSerializer.SerializeToElement(value, BridgeClient.JsonOptions);
        return new CallToolResult
        {
            IsError = isError,
            StructuredContent = json,
            Content = [new TextContentBlock { Text = json.GetRawText() }]
        };
    }
}

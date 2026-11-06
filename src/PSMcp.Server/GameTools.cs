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
    [Description("Check whether the authenticated Probably Automated game bridge is reachable. success means connected, not that a save is loaded. Use get_game_state for readiness. get_help works even if this connection check fails.")]
    public async Task<CallToolResult> GetBridgeStatus(CancellationToken cancellationToken) => Reply(await bridge.Health(cancellationToken));

    [McpServerTool(Name = "get_game_state", ReadOnly = true, OpenWorld = false)]
    [Description("Read a main-thread snapshot: phase, cash, rent, customer, dialogue, visible inventory, trade, controls, interaction and availableActions. Fields are under state. Copy state.availableActions[].name into perform_action.action and state.snapshotId into expectedSnapshotId; use only that action's parameters for arguments. public_claim features are unverified; quotes are UI prices, not true values. Re-read after actions/animations. See get_help(topic=actions) when unsure.")]
    public async Task<CallToolResult> GetGameState(CancellationToken cancellationToken) => Reply(await bridge.State(cancellationToken));

    [McpServerTool(Name = "get_trade_state", ReadOnly = true, OpenWorld = false)]
    [Description("Read the current trade, counter items, known/claimed features, UI quotes and interaction blockers. Directions are from the player's perspective. Quotes apply to the offered stack; do not multiply by count. Wholesale totals may include items outside the counter. Use only listed availableActions to trade, bargain or confirm.")]
    public async Task<CallToolResult> GetTradeState(CancellationToken cancellationToken)
    {
        var result = await bridge.State(cancellationToken);
        if (!result.Success || result.State == null) return Reply(result);
        return Content(new { result.ProtocolVersion, result.State.SnapshotId, result.State.GameLoaded,
            result.State.Customer, result.State.Trade, result.State.Interaction,
            result.State.Controls, result.State.AvailableActions, result.State.Limitations });
    }

    [McpServerTool(Name = "get_inventory", ReadOnly = true, OpenWorld = false)]
    [Description("Read rendered containers and their items: entityId, ownership, shape, position and known features. Opened nested containers appear separately; unopened contents are not read. Filter by a current containerId and paginate. This query does not use or move items: find current item actions via get_available_actions or get_entity, then perform_action. Use entityId candidates from parameters, not numeric item IDs. See get_help(topic=items).")]
    public async Task<CallToolResult> GetInventory(CancellationToken cancellationToken,
        [Description("Current container entityId, or omit for all rendered containers.")] string? containerId = null,
        int offset = 0, int limit = 100)
    {
        var result = await bridge.State(cancellationToken);
        if (!result.Success || result.State == null) return Reply(result);
        if (offset < 0 || limit < 1 || limit > 200) return Reply(BridgeResponse.Error("invalid_request", "offset must be nonnegative; limit must be 1..200."));
        if (containerId != null && !result.State.Containers.Any(c => c.EntityId == containerId))
            return Reply(BridgeResponse.Error("entity_unavailable", "This container is not currently rendered. Open it through an available action first."));
        var items = result.State.Inventory.Where(i => containerId == null || i.ContainerId == containerId).ToArray();
        return Content(new { result.ProtocolVersion, result.State.SnapshotId, result.State.GameLoaded, result.State.InventoryScope,
            Containers = result.State.Containers.Where(c => containerId == null || c.EntityId == containerId)
                .Select(c => new { c.EntityId, c.Role, c.ParentItemId, c.WindowTitle, c.Kind, c.InsertLocked, c.RemoveLocked, c.Shape, ItemCount = c.Items.Length }),
            Total = items.Length, Offset = offset, Inventory = items.Skip(offset).Take(limit) });
    }

    [McpServerTool(Name = "get_available_actions", ReadOnly = true, OpenWorld = false)]
    [Description("Return snapshotId, gameLoaded, availableActions, interaction, limitations and usage at the top level. Copy an exact action.name into perform_action.action; kind is its category, description explains it, and targetId identifies the object the action belongs to. Fill arguments only from that action's parameters (required, min/max/step, choices). For item_use_on, arguments.targetId is a legal destination target, distinct from action.targetId (the tool). Never invent dynamic action names or references. Empty lists require inspecting interaction, not guessing a command. See get_help(topic=actions).")]
    public async Task<CallToolResult> GetAvailableActions(CancellationToken cancellationToken)
    {
        var result = await bridge.State(cancellationToken);
        if (!result.Success || result.State == null) return Reply(result);
        return Content(new { result.ProtocolVersion, result.State.SnapshotId, result.State.GameLoaded, result.State.AvailableActions, result.State.DialogueChoices, result.State.Interaction,
            result.State.Limitations,
            Usage = new
            {
                Tool = "perform_action",
                Action = "Copy availableActions[].name exactly; kind, description and targetId are not action names.",
                ExpectedSnapshotId = "Use snapshotId from the observation on which this decision is based.",
                RequestId = "New UUID for a new decision. Retry an uncertain command only with the original action, snapshot, UUID and arguments unchanged.",
                Arguments = "Use only parameters listed by the chosen action. Respect required, min/max/step and choices. Omit arguments for parameterless actions. action.targetId identifies the source object; arguments.targetId may select a different legal target.",
                AfterDispatch = "Re-read state to verify the effect; dispatched does not mean animations or confirmations are complete.",
                EmptyList = "Inspect interaction.waitingFor, blockers and limitations. Wait for a transition or report the unsupported input; do not invent a command.",
                HelpTopic = "actions"
            } });
    }

    [McpServerTool(Name = "get_ui_state", ReadOnly = true, OpenWorld = false)]
    [Description("Read visible text and controls with labels, values, options, enabled state and actions. Match a control's actions to availableActions; use the exact action name and parameters. For duplicate labels compare windowTitle, nearbyText and bounds with get_view. Disabled/occluded controls cannot be invoked. Useful for confirmations, appraisal, machines, services and night panels; see get_help(topic=menus) or get_help(topic=appraisal).")]
    public async Task<CallToolResult> GetUiState(CancellationToken cancellationToken)
    {
        var result = await bridge.State(cancellationToken);
        if (!result.Success || result.State == null) return Reply(result);
        return Content(new { result.State.SnapshotId, result.State.Context, result.State.Interaction,
            result.State.ScreenText, result.State.Controls, result.State.AvailableActions });
    }

    [McpServerTool(Name = "get_entity", ReadOnly = true, OpenWorld = false)]
    [Description("Inspect a current exposed item, container or control by entityId. This query never opens containers or performs scans. References expire when their objects are no longer accessible. Read availableActions to interact with the entity.")]
    public async Task<CallToolResult> GetEntity(string entityId, CancellationToken cancellationToken)
    {
        var result = await bridge.State(cancellationToken);
        if (!result.Success || result.State == null) return Reply(result);
        object? entity = result.State.Inventory.FirstOrDefault(i => i.EntityId == entityId)
            ?? (object?)result.State.Containers.FirstOrDefault(c => c.EntityId == entityId)
            ?? result.State.Controls.FirstOrDefault(c => c.EntityId == entityId);
        if (entity == null) return Reply(BridgeResponse.Error("entity_unavailable", "This entity is not currently exposed; read the current state."));
        return Content(new { result.State.SnapshotId, Entity = entity,
            AvailableActions = result.State.AvailableActions.Where(a => a.TargetId == entityId) });
    }

    [McpServerTool(Name = "get_view", ReadOnly = true, OpenWorld = false)]
    [Description("Capture the rendered game frame, including inspection artwork, customer appearance and visible panels, as an MCP image with its same-frame snapshotId. Longest edge is at most 1600 pixels. Does not reveal hidden item truth or advance the game deliberately.")]
    public async Task<CallToolResult> GetView(CancellationToken cancellationToken)
    {
        var result = await bridge.View(cancellationToken);
        if (!result.Success || result.View == null) return Reply(result);
        var view = result.View;
        var metadata = JsonSerializer.SerializeToElement(new { result.ProtocolVersion, view.SnapshotId, view.Width, view.Height, view.MimeType,
            result.State?.Context, result.State?.Interaction }, BridgeClient.JsonOptions);
        return new CallToolResult { StructuredContent = metadata, Content = [new TextContentBlock { Text = metadata.GetRawText() },
            new ImageContentBlock { Data = System.Text.Encoding.UTF8.GetBytes(view.ImageBase64), MimeType = view.MimeType }] };
    }

    [McpServerTool(Name = "get_action_status", ReadOnly = true, OpenWorld = false)]
    [Description("Read the retained original result of a requestId without executing it again. Retains the last 256 commands until Mod restart. dispatched means native handler invoked, not animation/transaction completed; use get_game_state for current progress. action_not_found does not prove the action never ran.")]
    public async Task<CallToolResult> GetActionStatus(string requestId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(requestId, "D", out _)) return Reply(BridgeResponse.Error("invalid_request", "requestId must be a UUID."));
        return Reply(await bridge.ActionStatus(requestId, cancellationToken));
    }

    [McpServerTool(Name = "perform_action", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Dispatch one exact availableActions[].name with the same observation's snapshotId and a caller-generated UUID. arguments is a JSON object containing only the chosen action's parameters; omit for parameterless actions. For uncertain outcomes first inspect get_action_status and current state; retries must preserve action, expectedSnapshotId, requestId AND arguments. On stale_snapshot inspect state and make a new decision with a new UUID. dispatched means the handler ran, not that animations/confirmations finished. Actions affect progress and may be saved. See get_help(topic=actions).")]
    public async Task<CallToolResult> PerformAction(
        [Description("Copy a currently listed availableActions[].name exactly, including dynamic suffixes. Never use kind, description or an entity ID as the action.")] string action,
        [Description("snapshotId from the observation on which this decision is based.")] string expectedSnapshotId,
        [Description("New UUID for a new decision; reuse it with unchanged arguments only for retrying that same command.")] string requestId,
        CancellationToken cancellationToken,
        [Description("JSON object containing only parameters listed for this action: targetId, destinationId, quantity, x/y, rotation (0..3), flipped, value/upperValue, text, index or toggle. Omit for actions with no parameters.")] JsonElement? arguments = null)
    {
        if (!Guid.TryParseExact(requestId, "D", out _) || string.IsNullOrWhiteSpace(action)
            || action.Length > 128 || string.IsNullOrWhiteSpace(expectedSnapshotId) || expectedSnapshotId.Length > 128)
            return Reply(BridgeResponse.Error("invalid_request", "Provide a UUID requestId, a nonempty action and a nonempty snapshotId (at most 128 characters each)."));
        ActionArguments? parsedArguments = null;
        if (arguments.HasValue && arguments.Value.ValueKind != JsonValueKind.Null)
        {
            if (arguments.Value.ValueKind != JsonValueKind.Object)
                return Reply(BridgeResponse.Error("invalid_arguments", "arguments must be an object."));
            try
            {
                var strict = new JsonSerializerOptions(BridgeClient.JsonOptions)
                { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
                parsedArguments = arguments.Value.Deserialize<ActionArguments>(strict);
            }
            catch (JsonException) { return Reply(BridgeResponse.Error("invalid_arguments", "Use only recognized argument names and their documented types.")); }
        }
        return Reply(await bridge.Perform(new ActionRequest { Action = action, ExpectedSnapshotId = expectedSnapshotId, RequestId = requestId, Arguments = parsedArguments }, cancellationToken));
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

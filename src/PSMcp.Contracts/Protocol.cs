namespace PSMcp.Contracts;

public static class Protocol
{
    public const int Version = 2;
    public const int DefaultPort = 15527;
    public const string ModId = "probably-stolen-mcp";
}

public sealed class ConnectionInfo
{
    public int ProtocolVersion { get; set; } = Protocol.Version;
    public string BaseUrl { get; set; } = "";
    public string Token { get; set; } = "";
}

public sealed class GameSnapshot
{
    public string SnapshotId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public bool GameLoaded { get; set; }
    public string GameVersion { get; set; } = "";
    public string Phase { get; set; } = "main_menu";
    public int? Day { get; set; }
    public int? Cash { get; set; }
    public int? Rent { get; set; }
    public int? DaysUntilRent { get; set; }
    public CustomerInfo? Customer { get; set; }
    public TradeInfo? Trade { get; set; }
    public InteractionInfo Interaction { get; set; } = new InteractionInfo();
    public string[] RecentDialogue { get; set; } = Array.Empty<string>();
    public InventoryItem[] Inventory { get; set; } = Array.Empty<InventoryItem>();
    public string InventoryScope { get; set; } = "main_inventory_top_level";
    public InventoryContainer[] Containers { get; set; } = Array.Empty<InventoryContainer>();
    public GameContext Context { get; set; } = new GameContext();
    public UiControl[] Controls { get; set; } = Array.Empty<UiControl>();
    public string[] ScreenText { get; set; } = Array.Empty<string>();
    public DialogueChoice[] DialogueChoices { get; set; } = Array.Empty<DialogueChoice>();
    public AvailableAction[] AvailableActions { get; set; } = Array.Empty<AvailableAction>();
    public string[] Limitations { get; set; } = Array.Empty<string>();
}

public sealed class CustomerInfo
{
    public string VisitId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Intent { get; set; } = "";
    public int? Budget { get; set; }
}

public sealed class InventoryItem
{
    public string EntityId { get; set; } = "";
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public int Count { get; set; }
    public string ContainerId { get; set; } = "";
    public string Owner { get; set; } = "unknown";
    public string Description { get; set; } = "";
    public ItemFeatureInfo[] Features { get; set; } = Array.Empty<ItemFeatureInfo>();
    public GridInfo? Shape { get; set; }
    public bool ContentsOpen { get; set; }
}

public sealed class GameContext
{
    public string Location { get; set; } = "main_menu";
    public string Mode { get; set; } = "normal";
    public string DialogueState { get; set; } = "idle";
    public string[] InputLocks { get; set; } = Array.Empty<string>();
    public string[] ModalPanels { get; set; } = Array.Empty<string>();
}

public sealed class InventoryContainer
{
    public string EntityId { get; set; } = "";
    public string Role { get; set; } = "container";
    public string? ParentItemId { get; set; }
    public string WindowTitle { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool InsertLocked { get; set; }
    public bool RemoveLocked { get; set; }
    public GridInfo? Shape { get; set; }
    public InventoryItem[] Items { get; set; } = Array.Empty<InventoryItem>();
}

public sealed class GridInfo
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Rotation { get; set; }
    public bool Flipped { get; set; }
    public string[] Cells { get; set; } = Array.Empty<string>();
}

public sealed class UiControl
{
    public string EntityId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public bool Enabled { get; set; }
    public string NearbyText { get; set; } = "";
    public ScreenBounds? Bounds { get; set; }
    public string? DisabledReason { get; set; }
    public string? Text { get; set; }
    public double? Value { get; set; }
    public double? UpperValue { get; set; }
    public bool? IsOn { get; set; }
    public string[] Options { get; set; } = Array.Empty<string>();
    public string[] Actions { get; set; } = Array.Empty<string>();
}

// Normalized screenshot coordinates, with the origin at the top left.
public sealed class ScreenBounds
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class GameView
{
    public string SnapshotId { get; set; } = "";
    public string MimeType { get; set; } = "image/jpeg";
    public int Width { get; set; }
    public int Height { get; set; }
    public string ImageBase64 { get; set; } = "";
}

public sealed class TradeInfo
{
    public string CustomerVisitId { get; set; } = "";
    public string CustomerIntent { get; set; } = "";
    // All directions are from the player's perspective, unlike the game's ClientIntent.
    public string Direction { get; set; } = "other";
    public bool BusinessComplete { get; set; }
    public bool PanelOpen { get; set; }
    public bool Locked { get; set; }
    public int? NegotiatedItemId { get; set; }
    public string ItemScope { get; set; } = "negotiation_counter_top_level";
    public TradeItem[] Items { get; set; } = Array.Empty<TradeItem>();
    public TradeQuote[] Quotes { get; set; } = Array.Empty<TradeQuote>();
    public VisiblePanel[] Panels { get; set; } = Array.Empty<VisiblePanel>();
}

public sealed class TradeItem
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public int Count { get; set; }
    public string Owner { get; set; } = "";
    public string Description { get; set; } = "";
    public string LongDescription { get; set; } = "";
    public ItemFeatureInfo[] Features { get; set; } = Array.Empty<ItemFeatureInfo>();
    public string? RefusalReason { get; set; }
}

public sealed class ItemFeatureInfo
{
    public string Text { get; set; } = "";
    // public_claim is what the UI currently says, not a guarantee of authenticity.
    public string Knowledge { get; set; } = "public_claim";
}

public sealed class TradeQuote
{
    public string Kind { get; set; } = "item_price";
    public int? ItemId { get; set; }
    public string Direction { get; set; } = "";
    public long Amount { get; set; }
    public string Currency { get; set; } = "credits";
    // Never multiply this by Count: it is the game's quote for the offered item/stack.
    public string Scope { get; set; } = "item_as_offered";
    public string Source { get; set; } = "negotiation_ui_price_calculation";
}

public sealed class VisiblePanel
{
    public string Kind { get; set; } = "";
    public string[] Text { get; set; } = Array.Empty<string>();
    public VisibleControl[] Controls { get; set; } = Array.Empty<VisibleControl>();
}

public sealed class VisibleControl
{
    public string Text { get; set; } = "";
    public bool Enabled { get; set; }
    // UI observation only. An action is callable only if listed in AvailableActions.
}

public sealed class InteractionInfo
{
    public string Kind { get; set; } = "unknown";
    public string WaitingFor { get; set; } = "unknown";
    public string Message { get; set; } = "Interaction observation is unavailable; update the Mod and MCP together.";
    public string? RequiredInput { get; set; }
    public bool McpSupported { get; set; }
    public string[] Blockers { get; set; } = Array.Empty<string>();
    public VisiblePanel[] Panels { get; set; } = Array.Empty<VisiblePanel>();
}

public sealed class DialogueChoice
{
    public int Index { get; set; }
    public string Text { get; set; } = "";
    public bool Enabled { get; set; }
}

public sealed class AvailableAction
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? TargetId { get; set; }
    public ActionParameter[] Parameters { get; set; } = Array.Empty<ActionParameter>();
    public bool AllowWithoutGame { get; set; }
}

public sealed class ActionParameter
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public string[] Choices { get; set; } = Array.Empty<string>();
}

public sealed class ActionArguments
{
    public string? TargetId { get; set; }
    public string? DestinationId { get; set; }
    public int? Quantity { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? Rotation { get; set; }
    public bool? Flipped { get; set; }
    public double? Value { get; set; }
    public double? UpperValue { get; set; }
    public string? Text { get; set; }
    public int? Index { get; set; }
    public bool? Toggle { get; set; }

    public IEnumerable<KeyValuePair<string, object>> Values()
    {
        if (TargetId != null) yield return new KeyValuePair<string, object>("targetId", TargetId);
        if (DestinationId != null) yield return new KeyValuePair<string, object>("destinationId", DestinationId);
        if (Quantity.HasValue) yield return new KeyValuePair<string, object>("quantity", Quantity.Value);
        if (X.HasValue) yield return new KeyValuePair<string, object>("x", X.Value);
        if (Y.HasValue) yield return new KeyValuePair<string, object>("y", Y.Value);
        if (Rotation.HasValue) yield return new KeyValuePair<string, object>("rotation", Rotation.Value);
        if (Flipped.HasValue) yield return new KeyValuePair<string, object>("flipped", Flipped.Value);
        if (Value.HasValue) yield return new KeyValuePair<string, object>("value", Value.Value);
        if (UpperValue.HasValue) yield return new KeyValuePair<string, object>("upperValue", UpperValue.Value);
        if (Text != null) yield return new KeyValuePair<string, object>("text", Text);
        if (Index.HasValue) yield return new KeyValuePair<string, object>("index", Index.Value);
        if (Toggle.HasValue) yield return new KeyValuePair<string, object>("toggle", Toggle.Value);
    }
}

public sealed class ActionRequest
{
    public string RequestId { get; set; } = "";
    public string ExpectedSnapshotId { get; set; } = "";
    public string Action { get; set; } = "";
    public ActionArguments? Arguments { get; set; }
}

public sealed class BridgeResponse
{
    public int ProtocolVersion { get; set; } = Protocol.Version;
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string Message { get; set; } = "";
    public string? RequestId { get; set; }
    public string? ActionStatus { get; set; }
    public GameSnapshot? State { get; set; }
    public GameView? View { get; set; }

    public static BridgeResponse Ok(GameSnapshot? state = null, string message = "OK") =>
        new BridgeResponse { Success = true, State = state, Message = message };

    public static BridgeResponse Error(string code, string message, string? requestId = null) =>
        new BridgeResponse { ErrorCode = code, Message = message, RequestId = requestId };
}

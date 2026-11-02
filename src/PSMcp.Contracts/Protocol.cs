namespace PSMcp.Contracts;

public static class Protocol
{
    public const int Version = 1;
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
    public int? Cash { get; set; }
    public int? Rent { get; set; }
    public int? DaysUntilRent { get; set; }
    public CustomerInfo? Customer { get; set; }
    public string[] RecentDialogue { get; set; } = Array.Empty<string>();
    public InventoryItem[] Inventory { get; set; } = Array.Empty<InventoryItem>();
    public string InventoryScope { get; set; } = "main_inventory_top_level";
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
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public int Count { get; set; }
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
}

public sealed class ActionRequest
{
    public string RequestId { get; set; } = "";
    public string ExpectedSnapshotId { get; set; } = "";
    public string Action { get; set; } = "";
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

    public static BridgeResponse Ok(GameSnapshot? state = null, string message = "OK") =>
        new BridgeResponse { Success = true, State = state, Message = message };

    public static BridgeResponse Error(string code, string message, string? requestId = null) =>
        new BridgeResponse { ErrorCode = code, Message = message, RequestId = requestId };
}

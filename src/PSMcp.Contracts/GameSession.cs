namespace PSMcp.Contracts;

/// <summary>Implemented by the game adapter; every call must run on the game thread.</summary>
public interface IGameAdapter
{
    GameSnapshot Capture();
    void Execute(string action);
}

/// <summary>Serializes decisions against observations and deduplicates action retries.</summary>
public sealed class GameSession
{
    private readonly IGameAdapter adapter;
    private readonly Func<GameSnapshot, string> fingerprint;
    private readonly Dictionary<string, (string Action, string SnapshotId, BridgeResponse Result)> history = new();
    private readonly Queue<string> historyOrder = new();
    private string sessionId = Guid.NewGuid().ToString("N");
    private string? previousFingerprint;
    private long revision;

    public GameSession(IGameAdapter adapter, Func<GameSnapshot, string> fingerprint)
    {
        this.adapter = adapter;
        this.fingerprint = fingerprint;
    }

    public void Reset()
    {
        sessionId = Guid.NewGuid().ToString("N");
        previousFingerprint = null;
        revision = 0;
        // Retain recent request results across save changes: retries must never execute twice.
    }

    public GameSnapshot Observe()
    {
        var state = adapter.Capture();
        state.SessionId = sessionId;
        state.SnapshotId = "";
        var currentFingerprint = fingerprint(state);
        if (currentFingerprint != previousFingerprint)
        {
            revision++;
            previousFingerprint = currentFingerprint;
        }
        state.SnapshotId = sessionId + ":" + revision;
        return state;
    }

    public BridgeResponse Perform(ActionRequest request)
    {
        if (!Guid.TryParseExact(request.RequestId, "D", out _))
            return BridgeResponse.Error("invalid_request", "requestId must be a UUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).");
        if (string.IsNullOrWhiteSpace(request.ExpectedSnapshotId) || string.IsNullOrWhiteSpace(request.Action))
            return BridgeResponse.Error("invalid_request", "action and expectedSnapshotId are required.", request.RequestId);
        if (history.TryGetValue(request.RequestId, out var cached))
        {
            if (cached.Action != request.Action || cached.SnapshotId != request.ExpectedSnapshotId)
                return BridgeResponse.Error("request_id_conflict", "This requestId was used for a different command.", request.RequestId);
            return cached.Result;
        }

        var before = Observe();
        BridgeResponse result;
        if (before.SnapshotId != request.ExpectedSnapshotId)
        {
            result = BridgeResponse.Error("stale_snapshot", "The game changed. Read the current state and decide again with a new requestId.", request.RequestId);
            result.State = before;
        }
        else if (!before.GameLoaded || !before.AvailableActions.Any(x => x.Name == request.Action))
        {
            result = BridgeResponse.Error("action_unavailable", "This action is not currently available. Read availableActions.", request.RequestId);
            result.State = before;
        }
        else
        {
            // Invalidate the observation even if an action has delayed effects or throws part-way through.
            revision++;
            try
            {
                adapter.Execute(request.Action);
                result = BridgeResponse.Ok(Observe(), "Action dispatched. Read state again after animations or dialogue advance.");
                result.ActionStatus = "dispatched";
            }
            catch
            {
                result = BridgeResponse.Error("action_outcome_unknown", "The game handler failed; it may have applied part of the action. Read state before making another decision.");
                result.ActionStatus = "unknown";
            }
            result.RequestId = request.RequestId;
        }
        history[request.RequestId] = (request.Action, request.ExpectedSnapshotId, result);
        historyOrder.Enqueue(request.RequestId);
        if (historyOrder.Count > 256) history.Remove(historyOrder.Dequeue());
        return result;
    }
}

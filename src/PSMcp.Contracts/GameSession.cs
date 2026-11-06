namespace PSMcp.Contracts;

/// <summary>Implemented by the game adapter; every call must run on the game thread.</summary>
public interface IGameAdapter
{
    GameSnapshot Capture();
    // Prepare validates without side effects; the returned delegate performs one native action.
    Action Prepare(string action, ActionArguments arguments);
}

public interface IResettableGameAdapter
{
    void ResetReferences();
}

/// <summary>Serializes decisions against observations and deduplicates action retries.</summary>
public sealed class GameSession
{
    private readonly IGameAdapter adapter;
    private readonly Func<GameSnapshot, string> fingerprint;
    private readonly Dictionary<string, (string Action, string Arguments, string SnapshotId, BridgeResponse Result)> history = new();
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
        if (adapter is IResettableGameAdapter resettable) resettable.ResetReferences();
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
        if (string.IsNullOrWhiteSpace(request.ExpectedSnapshotId) || string.IsNullOrWhiteSpace(request.Action)
            || request.Action.Length > 128 || request.ExpectedSnapshotId.Length > 128)
            return BridgeResponse.Error("invalid_request", "action and expectedSnapshotId are required.", request.RequestId);
        request.RequestId = Guid.Parse(request.RequestId).ToString("D");
        var arguments = request.Arguments ?? new ActionArguments();
        var argumentFingerprint = ActionValidation.Fingerprint(arguments);
        if (history.TryGetValue(request.RequestId, out var cached))
        {
            if (cached.Action != request.Action || cached.Arguments != argumentFingerprint || cached.SnapshotId != request.ExpectedSnapshotId)
                return BridgeResponse.Error("request_id_conflict", "This requestId was used for a different command.", request.RequestId);
            return cached.Result;
        }

        var before = Observe();
        var capability = before.AvailableActions.FirstOrDefault(x => x.Name == request.Action);
        BridgeResponse result;
        if (before.SnapshotId != request.ExpectedSnapshotId)
        {
            result = BridgeResponse.Error("stale_snapshot", "The game changed. Read the current state and decide again with a new requestId.", request.RequestId);
            result.State = before;
        }
        else if (capability == null || (!before.GameLoaded && !capability.AllowWithoutGame))
        {
            result = BridgeResponse.Error("action_unavailable", "This action is not currently available. Read availableActions.", request.RequestId);
            result.State = before;
        }
        else
        {
            Action? execute = null;
            try
            {
                var error = ActionValidation.Validate(capability, arguments);
                if (error != null) throw new ActionRejectedException(error);
                execute = adapter.Prepare(request.Action, arguments);
                result = BridgeResponse.Ok();
            }
            catch (ActionRejectedException ex)
            {
                result = BridgeResponse.Error("invalid_arguments", ex.Message);
                result.ActionStatus = "rejected";
                result.State = before;
            }
            catch
            {
                result = BridgeResponse.Error("action_validation_failed", "The game could not validate this action. No action was dispatched.");
                result.ActionStatus = "rejected";
            }
            if (execute != null)
            {
                // Even a delayed or partially failed handler invalidates the decision.
                revision++;
                try
                {
                    execute();
                    result = BridgeResponse.Ok(Observe(), "Action dispatched. Read state again after animations or dialogue advance.");
                    result.ActionStatus = "dispatched";
                }
                catch
                {
                    result = BridgeResponse.Error("action_outcome_unknown", "The game handler failed; it may have applied part of the action. Read state before making another decision.");
                    result.ActionStatus = "unknown";
                }
            }
            result.RequestId = request.RequestId;
        }
        history[request.RequestId] = (request.Action, argumentFingerprint, request.ExpectedSnapshotId, result);
        historyOrder.Enqueue(request.RequestId);
        if (historyOrder.Count > 256) history.Remove(historyOrder.Dequeue());
        return result;
    }

    public BridgeResponse GetActionStatus(string requestId)
    {
        if (!Guid.TryParseExact(requestId, "D", out var id))
            return BridgeResponse.Error("invalid_request", "requestId must be a UUID.");
        return history.TryGetValue(id.ToString("D"), out var result) ? result.Result
            : BridgeResponse.Error("action_not_found", "No retained result for this requestId. The last 256 commands are retained until the Mod restarts; absence does not prove non-execution.", requestId);
    }
}

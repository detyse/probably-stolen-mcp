using System.Collections.Concurrent;

namespace PSMcp.Contracts;

/// <summary>Bounded queue. Expired queued actions can never run in a later frame.</summary>
public sealed class MainThreadQueue : IDisposable
{
    private readonly ConcurrentQueue<WorkTicket> queue = new();
    private readonly object sync = new();
    private bool stopped;

    public WorkTicket Enqueue(Func<BridgeResponse> work)
    {
        lock (sync)
        {
            if (stopped) return WorkTicket.Rejected("bridge_stopped", "The game bridge is shutting down.");
            if (queue.Count >= 64) return WorkTicket.Rejected("bridge_busy", "The game command queue is full.");
            var ticket = new WorkTicket(work);
            queue.Enqueue(ticket);
            return ticket;
        }
    }

    public void Drain(int maximum = 8)
    {
        for (var i = 0; i < maximum && queue.TryDequeue(out var ticket); i++) ticket.Run();
    }

    public void Dispose()
    {
        lock (sync)
        {
            stopped = true;
            while (queue.TryDequeue(out var ticket)) ticket.CancelPending("bridge_stopped", "The game bridge stopped before execution.");
        }
    }
}

public sealed class WorkTicket
{
    private readonly Func<BridgeResponse> work;
    private readonly TaskCompletionSource<BridgeResponse> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int state; // 0 = queued, 1 = running, 2 = finished or cancelled
    public Task<BridgeResponse> Completion => completion.Task;

    internal WorkTicket(Func<BridgeResponse> work) => this.work = work;

    internal static WorkTicket Rejected(string code, string message)
    {
        var ticket = new WorkTicket(() => throw new InvalidOperationException());
        ticket.CancelPending(code, message);
        return ticket;
    }

    public bool CancelPending(string code = "queue_timeout", string message = "The game did not process the request in time. No action was executed.")
    {
        if (Interlocked.CompareExchange(ref state, 2, 0) != 0) return false;
        completion.TrySetResult(BridgeResponse.Error(code, message));
        return true;
    }

    internal void Run()
    {
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
        try { completion.TrySetResult(work()); }
        catch { completion.TrySetResult(BridgeResponse.Error("game_error", "Unable to read the game. Check the Probably Stolen MCP game log.")); }
        finally { Interlocked.Exchange(ref state, 2); }
    }
}

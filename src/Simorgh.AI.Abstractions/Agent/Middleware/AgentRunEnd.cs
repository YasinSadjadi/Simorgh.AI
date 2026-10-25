namespace Simorgh.AI.Abstractions.Agent.Middleware;

/// <summary>How a run ended: it produced a final answer, failed, or was cancelled.</summary>
public enum AgentRunEndReason
{
    /// <summary>The run produced a final answer.</summary>
    Completed,

    /// <summary>The run failed with an exception (<see cref="AgentRunEnd.Error" />).</summary>
    Failed,

    /// <summary>The run was cancelled before finishing.</summary>
    Cancelled
}

/// <summary>
///     Why and how a run ended; passed to <see cref="AgentMiddleware.OnEndAsync" /> so every
///     middleware sees the outcome — including failures and cancellations, not just the
///     successful path.
/// </summary>
public sealed class AgentRunEnd
{
    /// <param name="reason">How the run ended.</param>
    /// <param name="error">
    ///     The exception that failed the run, or the cancellation that stopped it;
    ///     <c>null</c> when the run completed.
    /// </param>
    /// <param name="cancelReason">Reason supplied by whoever requested the cancellation, when any.</param>
    public AgentRunEnd(AgentRunEndReason reason, Exception? error = null, string? cancelReason = null)
    {
        Reason = reason;
        Error = error;
        CancelReason = cancelReason;
    }

    /// <summary>How the run ended.</summary>
    public AgentRunEndReason Reason { get; }

    /// <summary>
    ///     The exception that failed the run, or the cancellation that stopped it;
    ///     <c>null</c> when the run completed.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>Reason supplied by whoever requested the cancellation, when any.</summary>
    public string? CancelReason { get; }

    /// <summary><c>true</c> when the run ended in <see cref="AgentRunEndReason.Completed" />.</summary>
    public bool IsCompleted => Reason == AgentRunEndReason.Completed;
}

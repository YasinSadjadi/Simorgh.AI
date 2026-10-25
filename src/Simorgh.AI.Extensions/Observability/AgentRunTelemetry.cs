namespace Simorgh.AI.Extensions.Observability;

/// <summary>
///     Aggregate telemetry for one completed agent run: timings, tool usage, token totals,
///     and the failure reason when the run did not succeed. Produced by
///     <see cref="AgentTelemetryMiddleware" /> when a run ends.
/// </summary>
public sealed class AgentRunTelemetry
{
    /// <summary>Id of the run this telemetry describes.</summary>
    public string RunId { get; internal set; } = string.Empty;

    /// <summary>Wall-clock duration of the whole run.</summary>
    public TimeSpan Duration { get; internal set; }

    /// <summary>Number of model rounds executed (one per model call, including tool-call rounds).</summary>
    public int ModelRounds { get; internal set; }

    /// <summary>Total time spent waiting on model calls.</summary>
    public TimeSpan ModelDuration { get; internal set; }

    /// <summary>Number of tool invocations performed.</summary>
    public int ToolCalls { get; internal set; }

    /// <summary>Number of tool invocations that returned an error.</summary>
    public int ToolErrors { get; internal set; }

    /// <summary>Total time spent executing tools.</summary>
    public TimeSpan ToolDuration { get; internal set; }

    /// <summary>Tokens consumed on the input side, summed across rounds.</summary>
    public long InputTokens { get; internal set; }

    /// <summary>Tokens produced on the output side, summed across rounds.</summary>
    public long OutputTokens { get; internal set; }

    /// <summary>Sum of <see cref="InputTokens" /> and <see cref="OutputTokens" />.</summary>
    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>The error that failed the run, or <c>null</c> when it completed or was cancelled.</summary>
    public string? Error { get; internal set; }

    /// <summary><c>true</c> when the run was cancelled before completing.</summary>
    public bool Cancelled { get; internal set; }
}

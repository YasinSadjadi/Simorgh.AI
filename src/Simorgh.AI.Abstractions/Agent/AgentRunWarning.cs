namespace Simorgh.AI.Abstractions.Agent;

/// <summary>
///     A non-fatal problem observed during a run — typically a middleware that failed and
///     was skipped. Reported through <see cref="AgentRunContext.ReportWarning" /> and
///     surfaced to run consumers as an <see cref="AgentRunWarningUpdate" />.
/// </summary>
/// <param name="source">What produced the warning, e.g. the middleware type name.</param>
/// <param name="message">Human-readable description of the problem.</param>
/// <param name="error">The underlying exception, when any.</param>
public sealed class AgentRunWarning(string source, string message, Exception? error = null)
{
    /// <summary>What produced the warning, e.g. the middleware type name.</summary>
    public string Source { get; } = source;

    /// <summary>Human-readable description of the problem.</summary>
    public string Message { get; } = message;

    /// <summary>The underlying exception, when any.</summary>
    public Exception? Error { get; } = error;
}

namespace Simorgh.AI.Abstractions.Agent.Middleware;

/// <summary>
///     The result a middleware hook returns instead of throwing. <see cref="Ok" /> lets
///     the run carry on; <see cref="Warn(string, Exception?)" /> records a warning —
///     surfaced to run consumers as an <see cref="Agent.AgentRunWarningUpdate" /> — while
///     the run carries on; <see cref="Fail(Exception)" /> breaks the run and moves it to
///     the end state with reason <see cref="AgentRunEndReason.Failed" />.
/// </summary>
public class AgentResult
{
    private protected AgentResult(Exception? error, string? warningMessage, Exception? warningDetail)
    {
        Error = error;
        WarningMessage = warningMessage;
        WarningDetail = warningDetail;
    }

    /// <summary>
    ///     The error that breaks the run; <c>null</c> unless the result is a
    ///     <see cref="Fail(Exception)" />.
    /// </summary>
    public Exception? Error { get; }

    /// <summary><c>true</c> when the result does not break the run.</summary>
    public bool Succeeded => Error is null;

    /// <summary>Description of the recorded warning; <c>null</c> when there is none.</summary>
    public string? WarningMessage { get; }

    /// <summary>Detail recorded alongside <see cref="WarningMessage" />, when any.</summary>
    public Exception? WarningDetail { get; }

    /// <summary><c>true</c> when the result records a warning.</summary>
    public bool HasWarning => WarningMessage is not null;

    /// <summary>The run carries on with nothing to report.</summary>
    public static AgentResult Ok()
    {
        return new AgentResult(null, null, null);
    }

    /// <summary>
    ///     Records a warning — the run carries on. The warning is reported on the run
    ///     context and reaches the run's consumers as an
    ///     <see cref="Agent.AgentRunWarningUpdate" />.
    /// </summary>
    /// <param name="message">Human-readable description of the problem.</param>
    /// <param name="detail">The underlying exception, when any.</param>
    public static AgentResult Warn(string message, Exception? detail = null)
    {
        return new AgentResult(null, message, detail);
    }

    /// <summary>Breaks the run: it fails and moves to the end state.</summary>
    public static AgentResult Fail(Exception error)
    {
        return new AgentResult(error, null, null);
    }

    /// <summary>Breaks the run: it fails and moves to the end state.</summary>
    public static AgentResult Fail(string message)
    {
        return new AgentResult(new Exception(message), null, null);
    }
}

/// <summary>
///     The result of a middleware hook that produces a value: <see cref="Value" /> is what
///     the next middleware (and finally the agent service) consumes, unless the result is
///     a <see cref="Fail(Exception)" />, which breaks the run. A warning can be recorded
///     alongside the value with <see cref="Warn(T, string, Exception?)" />.
/// </summary>
/// <typeparam name="T">Type of the value the hook threads through the chain.</typeparam>
public sealed class AgentResult<T> : AgentResult
{
    private AgentResult(T? value, Exception? error, string? warningMessage, Exception? warningDetail)
        : base(error, warningMessage, warningDetail)
    {
        Value = value;
    }

    /// <summary>
    ///     The value for the rest of the chain; <c>null</c>/<c>default</c> only when the
    ///     result is a <see cref="Fail(Exception)" />.
    /// </summary>
    public T? Value { get; }

    /// <summary>The run carries on with <paramref name="value" />.</summary>
    public static AgentResult<T> Ok(T value)
    {
        return new AgentResult<T>(value, null, null, null);
    }

    /// <summary>
    ///     Records a warning while carrying on with <paramref name="value" />. To simply
    ///     pass a stage's input through unchanged (the equivalent of skipping this
    ///     middleware), return the input as the value.
    /// </summary>
    /// <param name="value">The value the chain continues with.</param>
    /// <param name="message">Human-readable description of the problem.</param>
    /// <param name="detail">The underlying exception, when any.</param>
    public static AgentResult<T> Warn(T value, string message, Exception? detail = null)
    {
        return new AgentResult<T>(value, null, message, detail);
    }

    /// <summary>
    ///     Breaks the run: it fails and moves to the end state. The stage value is not
    ///     consumed.
    /// </summary>
    public static new AgentResult<T> Fail(Exception error)
    {
        return new AgentResult<T>(default, error, null, null);
    }

    /// <summary>
    ///     Breaks the run: it fails and moves to the end state. The stage value is not
    ///     consumed.
    /// </summary>
    public static new AgentResult<T> Fail(string message)
    {
        return new AgentResult<T>(default, new Exception(message), null, null);
    }
}

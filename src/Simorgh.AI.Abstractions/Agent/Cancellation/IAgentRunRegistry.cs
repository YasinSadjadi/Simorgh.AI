namespace Simorgh.AI.Abstractions.Agent.Cancellation;

/// <summary>
///     Tracks submitted agent runs so they can be cancelled by run id — from the server
///     that owns them or from any other server in the deployment. Runs are registered
///     by the agent service on submission and removed when they finish.
/// </summary>
public interface IAgentRunRegistry
{
    /// <summary>Ids of the runs currently registered on this server.</summary>
    IReadOnlyCollection<string> ActiveRunIds { get; }

    /// <summary>Raised when a run is registered, on an unspecified thread.</summary>
    event Action<string>? RunRegistered;

    /// <summary>Raised when a registered run finishes and is removed, on an unspecified thread.</summary>
    event Action<string>? RunRemoved;

    /// <summary>
    ///     Registers a run that is executing on this server and can be cancelled through
    ///     <paramref name="cancel" />. Dispose of the returned lease when the run finishes
    ///     to remove it from the registry.
    /// </summary>
    /// <param name="runId">Id of the run.</param>
    /// <param name="cancel">Invoked with an optional reason when the run should be cancelled.</param>
    /// <returns>A lease that removes the registration when disposed.</returns>
    IDisposable Register(string runId, Action<string?> cancel);

    /// <summary>
    ///     Requests cancellation of the run with the given id. The request is broadcast so
    ///     that whichever server owns the run cancels it; <paramref name="reason" /> is
    ///     surfaced through <see cref="AgentRunCancelledUpdate" />.
    /// </summary>
    /// <param name="runId">Id of the run to cancel.</param>
    /// <param name="reason">Optional reason for the cancellation.</param>
    /// <param name="cancellationToken">Token to cancel the request operation.</param>
    /// <returns><c>true</c> if the run is currently registered on this server; otherwise <c>false</c>.</returns>
    Task<bool> CancelAsync(string runId, string? reason = null, CancellationToken cancellationToken = default);
}

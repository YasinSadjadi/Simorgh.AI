namespace Simorgh.AI.Abstractions.Agent.Cancellation;

/// <summary>
///     Publishes cancellation requests for agent runs so that every server in a
///     multi-server deployment can cancel the runs it owns. Implementations backed by
///     a distributed transport (e.g. Redis pub/sub) make cancellation work across
///     servers; the in-memory implementation in <c>Simorgh.AI.Extensions</c> only
///     works within a single process.
/// </summary>
public interface IAgentCancellationBroadcaster
{
    /// <summary>Raised when a cancellation request arrives, on an unspecified thread.</summary>
    /// <remarks>Receives the run id and an optional reason for the cancellation.</remarks>
    event Action<string, string?>? CancelRequested;

    /// <summary>
    ///     Publishes a cancellation request for the given run to all subscribers,
    ///     including those on other servers when the transport is distributed.
    /// </summary>
    /// <param name="runId">Id of the run to cancel.</param>
    /// <param name="reason">Optional reason carried to the owner of the run.</param>
    /// <param name="cancellationToken">Token to cancel the publish operation.</param>
    Task PublishCancelAsync(string runId, string? reason = null, CancellationToken cancellationToken = default);
}

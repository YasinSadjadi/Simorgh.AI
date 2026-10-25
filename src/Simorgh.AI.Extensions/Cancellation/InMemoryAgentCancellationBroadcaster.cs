using Simorgh.AI.Abstractions.Agent.Cancellation;

namespace Simorgh.AI.Extensions.Cancellation;

/// <summary>
///     In-process <see cref="IAgentCancellationBroadcaster" />. Cancellation requests only
///     reach subscribers in the same process; for a multi-server deployment, replace it with
///     a transport-backed implementation (e.g. Redis pub/sub).
/// </summary>
public sealed class InMemoryAgentCancellationBroadcaster : IAgentCancellationBroadcaster
{
    /// <inheritdoc />
    public event Action<string, string?>? CancelRequested;

    /// <inheritdoc />
    public Task PublishCancelAsync(
        string runId, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(runId)) throw new ArgumentException("Run id must not be empty.", nameof(runId));
        cancellationToken.ThrowIfCancellationRequested();
        CancelRequested?.Invoke(runId, reason);
        return Task.CompletedTask;
    }
}

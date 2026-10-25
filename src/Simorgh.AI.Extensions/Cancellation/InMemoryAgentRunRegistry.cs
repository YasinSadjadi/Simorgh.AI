using System.Collections.Concurrent;
using Simorgh.AI.Abstractions.Agent.Cancellation;

namespace Simorgh.AI.Extensions.Cancellation;

/// <summary>
///     In-process <see cref="IAgentRunRegistry" />. Runs submitted by this process are tracked
///     in memory and can be cancelled by id. Pair it with a distributed
///     <see cref="IAgentCancellationBroadcaster" /> so that cancellation requests issued on one
///     server cancel runs executing on another.
/// </summary>
/// <param name="broadcaster">
///     Transport used to publish cancellation requests. When omitted, an
///     <see cref="InMemoryAgentCancellationBroadcaster" /> is used, which limits cancellation
///     to the local process.
/// </param>
public sealed class InMemoryAgentRunRegistry(IAgentCancellationBroadcaster? broadcaster = null)
    : IAgentRunRegistry
{
    private readonly IAgentCancellationBroadcaster _broadcaster = broadcaster ?? new InMemoryAgentCancellationBroadcaster();
    private readonly ConcurrentDictionary<string, Action<string?>> _runs = new();

    /// <inheritdoc />
    public IReadOnlyCollection<string> ActiveRunIds => (IReadOnlyCollection<string>)_runs.Keys;

    /// <inheritdoc />
    public event Action<string>? RunRegistered;

    /// <inheritdoc />
    public event Action<string>? RunRemoved;

    /// <summary>Constructs the registry and starts listening for broadcast cancellation requests.</summary>
    public InMemoryAgentRunRegistry() : this(null)
    {
    }

    /// <inheritdoc />
    public IDisposable Register(string runId, Action<string?> cancel)
    {
        ValidateRunId(runId);
        if (cancel is null) throw new ArgumentNullException(nameof(cancel));
        if (!_runs.TryAdd(runId, cancel))
            throw new InvalidOperationException(
                $"A run with id '{runId}' is already registered. Run ids must be unique among active runs.");
        _broadcaster.CancelRequested += OnCancelRequested;
        RunRegistered?.Invoke(runId);
        return new Registration(this, runId);
    }

    /// <inheritdoc />
    public async Task<bool> CancelAsync(
        string runId, string? reason = null, CancellationToken cancellationToken = default)
    {
        ValidateRunId(runId);
        var isLocal = _runs.ContainsKey(runId);
        // Broadcast so the server that owns the run cancels it; this process cancels its own
        // run through the same subscription.
        await _broadcaster.PublishCancelAsync(runId, reason, cancellationToken).ConfigureAwait(false);
        return isLocal;
    }

    private static void ValidateRunId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("Run id must not be null, empty, or whitespace.", nameof(runId));
    }

    private void OnCancelRequested(string runId, string? reason)
    {
        if (_runs.TryGetValue(runId, out var cancel))
            cancel(reason);
    }

    private sealed class Registration(InMemoryAgentRunRegistry registry, string runId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            registry._runs.TryRemove(runId, out _);
            registry._broadcaster.CancelRequested -= registry.OnCancelRequested;
            registry.RunRemoved?.Invoke(runId);
        }
    }
}

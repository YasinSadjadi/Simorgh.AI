using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Abstractions.Agent;

/// <summary>
///     Shared state for one agent run, passed to every middleware hook.
/// </summary>
/// <param name="runId">Unique id of the run.</param>
/// <param name="messages">The conversation so far; middleware may inspect and modify it.</param>
/// <param name="cancellationToken">Token used to cancel the run.</param>
public sealed class AgentRunContext(
    string runId,
    IList<AiMessage> messages,
    CancellationToken cancellationToken = default)
{
    /// <summary>Unique id of the run.</summary>
    public string RunId { get; } = runId;

    /// <summary>
    ///     The conversation so far. Middleware may inspect and modify it;
    ///     the response of each round is appended by the pipeline.
    /// </summary>
    public IList<AiMessage> Messages { get; } = messages;

    /// <summary>Token used to cancel the run.</summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;

    /// <summary>Values produced or consumed by middleware during the run.</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
}
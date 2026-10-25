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

    /// <summary>
    ///     Total token capacity of the model's context window, when known. Set it (e.g.
    ///     from <c>OnStartupAsync</c>) to make <see cref="ContextUsagePercent" /> meaningful.
    /// </summary>
    public long? ContextWindowTokens { get; set; }

    /// <summary>
    ///     Tokens the model was actually given in the most recent model round, as reported
    ///     by the provider (input-token usage). <c>null</c> until the first response
    ///     reports usage.
    /// </summary>
    public long? ContextTokensUsed { get; private set; }

    /// <summary>
    ///     <see cref="ContextTokensUsed" /> as a percentage of
    ///     <see cref="ContextWindowTokens" /> (0-100), or <c>null</c> when either the used
    ///     tokens or the window size are not yet known.
    /// </summary>
    public double? ContextUsagePercent
    {
        get
        {
            var used = ContextTokensUsed;
            var window = ContextWindowTokens;
            return used.HasValue && window is > 0
                ? Math.Round(used.Value * 100.0 / window.Value, 2)
                : null;
        }
    }

    /// <summary>
    ///     Records the input token count the provider reported for a model round; that
    ///     count is the size of the context the model just saw.
    /// </summary>
    public void ReportContextTokens(long inputTokens)
    {
        ContextTokensUsed = inputTokens;
    }
}
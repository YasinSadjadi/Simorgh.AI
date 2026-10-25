using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Abstractions.Agent.Middleware;

/// <summary>
///     Token usage reported by the model for one response: input, output, cached input,
///     reasoning output, and any additional detail the provider reports.
/// </summary>
public sealed class AgentResponseUsage(
    long inputTokens,
    long outputTokens,
    long? cachedInputTokens = null,
    long? reasoningOutputTokens = null,
    IReadOnlyDictionary<string, long>? inputTokenDetails = null,
    IReadOnlyDictionary<string, long>? outputTokenDetails = null)
{
    /// <summary>Tokens consumed on the input side.</summary>
    public long InputTokens { get; } = inputTokens;

    /// <summary>Tokens produced on the output side.</summary>
    public long OutputTokens { get; } = outputTokens;

    /// <summary>Sum of <see cref="InputTokens" /> and <see cref="OutputTokens" />.</summary>
    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>Input tokens served from the provider's cache, when reported.</summary>
    public long? CachedInputTokens { get; } = cachedInputTokens;

    /// <summary>Output tokens spent on reasoning, when reported.</summary>
    public long? ReasoningOutputTokens { get; } = reasoningOutputTokens;

    /// <summary>Detail per input modality (text, image, audio, ...), when the provider reports it.</summary>
    public IReadOnlyDictionary<string, long>? InputTokenDetails { get; } = inputTokenDetails;

    /// <summary>Detail per output modality (text, audio, ...), when the provider reports it.</summary>
    public IReadOnlyDictionary<string, long>? OutputTokenDetails { get; } = outputTokenDetails;

    /// <summary>Extracts usage from the <see cref="AiUsageContent" /> carried by a response, if any.</summary>
    public static AgentResponseUsage? FromMessage(AiMessage response)
    {
        var content = response.Contents.OfType<AiUsageContent>().FirstOrDefault();
        if (content is null) return null;

        return new AgentResponseUsage(
            content.InputTokens,
            content.OutputTokens,
            content.InputTokenDetails?.TryGetValue("cached", out var cached) == true ? cached : null,
            content.OutputTokenDetails?.TryGetValue("reasoning", out var reasoning) == true ? reasoning : null,
            content.InputTokenDetails,
            content.OutputTokenDetails);
    }
}

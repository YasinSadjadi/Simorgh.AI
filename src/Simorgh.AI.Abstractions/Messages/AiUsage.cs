namespace Simorgh.AI.Abstractions.Messages;

/// <summary>
///     Token accounting reported by the provider for one completion. Carried on
///     <see cref="AiMessage.Usage" /> — it is metadata about the response, not a content
///     part, and is never sent back to the model as input.
/// </summary>
/// <param name="inputTokens">Tokens consumed on the input side.</param>
/// <param name="outputTokens">Tokens produced on the output side.</param>
/// <param name="inputTokenDetails">Detail per input modality (text, image, audio, cached), when the provider reports it.</param>
/// <param name="outputTokenDetails">Detail per output modality (text, audio, reasoning), when the provider reports it.</param>
public sealed class AiUsage(
    long inputTokens,
    long outputTokens,
    IReadOnlyDictionary<string, long>? inputTokenDetails = null,
    IReadOnlyDictionary<string, long>? outputTokenDetails = null)
{
    /// <summary>Tokens consumed on the input side.</summary>
    public long InputTokens { get; } = inputTokens;

    /// <summary>Tokens produced on the output side.</summary>
    public long OutputTokens { get; } = outputTokens;

    /// <summary>Sum of <see cref="InputTokens" /> and <see cref="OutputTokens" />.</summary>
    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>Detail per input modality (text, image, audio, cached), when the provider reports it.</summary>
    public IReadOnlyDictionary<string, long>? InputTokenDetails { get; } = inputTokenDetails;

    /// <summary>Detail per output modality (text, audio, reasoning), when the provider reports it.</summary>
    public IReadOnlyDictionary<string, long>? OutputTokenDetails { get; } = outputTokenDetails;
}

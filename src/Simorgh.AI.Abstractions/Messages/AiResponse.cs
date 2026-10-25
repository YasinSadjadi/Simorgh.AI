namespace Simorgh.AI.Abstractions.Messages;

/// <summary>
///     A model response: the message it produced, alongside the metadata the provider
///     reported for it. The metadata travels next to the message — never inside it — so it
///     is never sent back to the model as input.
/// </summary>
/// <param name="message">The message the model produced.</param>
/// <param name="usage">Token usage the provider reported for this response, when any.</param>
/// <param name="finishReason">
///     Provider-reported reason the response ended (e.g. <c>stop</c>, <c>length</c>,
///     <c>content_filter</c>), when reported.
/// </param>
public sealed class AiResponse(
    AiMessage message,
    AiUsage? usage = null,
    string? finishReason = null)
{
    /// <summary>The message the model produced.</summary>
    public AiMessage Message { get; } = message;

    /// <summary>Token usage the provider reported for this response, when any.</summary>
    public AiUsage? Usage { get; } = usage;

    /// <summary>
    ///     Provider-reported reason the response ended (e.g. <c>stop</c>, <c>length</c>,
    ///     <c>content_filter</c>), when reported.
    /// </summary>
    public string? FinishReason { get; } = finishReason;
}

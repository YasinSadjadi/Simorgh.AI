namespace Simorgh.AI.Abstractions.Contents;

/// <summary>
///     Model-internal reasoning produced by reasoning/thinking models. Not part of the visible reply.
/// </summary>
/// <param name="text">The reasoning trace.</param>
/// <param name="encryptedData">Opaque encrypted form of the reasoning, supplied by some providers for round-tripping.</param>
public sealed class AiThinkingContent(string text, string? encryptedData = null) : AiContent
{
    /// <summary>The reasoning trace.</summary>
    public string Text { get; } = text;

    /// <summary>Opaque encrypted form of the reasoning, supplied by some providers for round-tripping.</summary>
    public string? EncryptedData { get; } = encryptedData;
}
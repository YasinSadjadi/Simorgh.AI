namespace Simorgh.AI.Abstractions.Contents;

/// <summary>
///     A provider or pipeline error surfaced in the content stream (e.g. a failed content filter).
/// </summary>
/// <param name="message">Human-readable error description.</param>
/// <param name="errorCode">Machine-readable error code, when the provider supplies one.</param>
public sealed class AiErrorContent(string message, string? errorCode = null) : AiContent
{
    /// <summary>Human-readable error description.</summary>
    public string Message { get; } = message;

    /// <summary>Machine-readable error code, when the provider supplies one.</summary>
    public string? ErrorCode { get; } = errorCode;
}
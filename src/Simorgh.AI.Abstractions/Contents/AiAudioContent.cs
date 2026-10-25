namespace Simorgh.AI.Abstractions.Contents;

/// <summary>
///     Audio content (input audio, generated audio, or audio attached to a message).
/// </summary>
/// <param name="mimeType">IANA media type of the audio payload, e.g. <c>audio/wav</c>.</param>
/// <param name="data">Inline binary payload, when the audio is not referenced remotely.</param>
/// <param name="url">Remote location of the audio, when the payload is not inline.</param>
/// <param name="transcript">Transcript of the audio, when available (ASR output or provider-supplied).</param>
/// <param name="durationSeconds">Duration of the audio in seconds, when known.</param>
public sealed class AiAudioContent(
    string mimeType,
    ReadOnlyMemory<byte>? data = null,
    Uri? url = null,
    string? transcript = null,
    double? durationSeconds = null) : AiMediaContent(mimeType, data, url)
{
    /// <summary>Transcript of the audio, when available (ASR output or provider-supplied).</summary>
    public string? Transcript { get; } = transcript;

    /// <summary>Duration of the audio in seconds, when known.</summary>
    public double? DurationSeconds { get; } = durationSeconds;
}
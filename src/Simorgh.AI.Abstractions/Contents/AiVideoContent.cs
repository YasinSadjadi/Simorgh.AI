namespace Simorgh.AI.Abstractions.Contents;

/// <summary>
///     Video content (input video or generated video).
/// </summary>
/// <param name="mimeType">IANA media type of the video payload, e.g. <c>video/mp4</c>.</param>
/// <param name="data">Inline binary payload, when the video is not referenced remotely.</param>
/// <param name="url">Remote location of the video, when the payload is not inline.</param>
/// <param name="durationSeconds">Duration of the video in seconds, when known.</param>
/// <param name="caption">Optional caption or description of the video.</param>
public sealed class AiVideoContent(
    string mimeType,
    ReadOnlyMemory<byte>? data = null,
    Uri? url = null,
    double? durationSeconds = null,
    string? caption = null) : AiMediaContent(mimeType, data, url)
{
    /// <summary>Duration of the video in seconds, when known.</summary>
    public double? DurationSeconds { get; } = durationSeconds;

    /// <summary>Optional caption or description of the video.</summary>
    public string? Caption { get; } = caption;
}
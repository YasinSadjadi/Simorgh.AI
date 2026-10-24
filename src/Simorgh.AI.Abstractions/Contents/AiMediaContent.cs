namespace Simorgh.AI.Abstractions.Contents;

/// <summary>
///     Base type for binary media content. The payload is either inline (<see cref="Data" />)
///     or referenced by <see cref="Url" />; exactly one of the two should be set.
/// </summary>
/// <param name="mimeType">IANA media type of the payload, e.g. <c>image/png</c>, <c>audio/wav</c>.</param>
/// <param name="data">Inline binary payload, when the media is not referenced remotely.</param>
/// <param name="url">Remote location of the media, when the payload is not inline.</param>
public abstract class AiMediaContent(string mimeType, ReadOnlyMemory<byte>? data = null, Uri? url = null)
    : AiContent
{
    /// <summary>IANA media type of the payload, e.g. <c>image/png</c>, <c>audio/wav</c>.</summary>
    public string MimeType { get; } = mimeType;

    /// <summary>Inline binary payload, when the media is not referenced remotely.</summary>
    public ReadOnlyMemory<byte>? Data { get; } = data;

    /// <summary>Remote location of the media, when the payload is not inline.</summary>
    public Uri? Url { get; } = url;

    public bool IsInline => Data.HasValue;
}
namespace Simorgh.AI.Abstractions.Contents;

/// <summary>
///     Image content. Payload is inline (<see cref="AiMediaContent.Data" />) or remote (<see cref="AiMediaContent.Url" />
///     ).
/// </summary>
/// <param name="mimeType">IANA media type of the image payload, e.g. <c>image/png</c>.</param>
/// <param name="data">Inline binary payload, when the image is not referenced remotely.</param>
/// <param name="url">Remote location of the image, when the payload is not inline.</param>
/// <param name="altText">Optional accessibility text describing the image.</param>
/// <param name="extractedText">OCR text already extracted from the image, if any.</param>
public sealed class AiImageContent(
    string mimeType,
    ReadOnlyMemory<byte>? data = null,
    Uri? url = null,
    string? altText = null,
    string? extractedText = null) : AiMediaContent(mimeType, data, url)
{
    /// <summary>Optional accessibility text describing the image.</summary>
    public string? AltText { get; } = altText;

    /// <summary>OCR text already extracted from the image, if any.</summary>
    public string? ExtractedText { get; } = extractedText;
}
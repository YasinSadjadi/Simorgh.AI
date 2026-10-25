namespace Simorgh.AI.Abstractions.Contents;

/// <summary>
///     Generic file/document content (PDF, CSV, office documents, ...).
/// </summary>
/// <param name="mimeType">IANA media type of the file payload, e.g. <c>application/pdf</c>.</param>
/// <param name="data">Inline binary payload, when the file is not referenced remotely.</param>
/// <param name="url">Remote location of the file, when the payload is not inline.</param>
/// <param name="fileName">Name of the file, when known.</param>
/// <param name="sizeBytes">Size of the payload in bytes, when known.</param>
public sealed class AiFileContent(
    string mimeType,
    ReadOnlyMemory<byte>? data = null,
    Uri? url = null,
    string? fileName = null,
    long? sizeBytes = null) : AiMediaContent(mimeType, data, url)
{
    /// <summary>Name of the file, when known.</summary>
    public string? FileName { get; } = fileName;

    /// <summary>Size of the payload in bytes, when known.</summary>
    public long? SizeBytes { get; } = sizeBytes;
}
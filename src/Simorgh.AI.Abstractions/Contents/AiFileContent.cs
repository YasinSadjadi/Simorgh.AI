namespace Simorgh.AI.Abstractions.Contents;

/// <summary>
///     Generic file/document content (PDF, CSV, office documents, ...).
/// </summary>
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
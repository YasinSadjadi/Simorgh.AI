namespace Simorgh.AI.Abstractions.Contents;

/// <summary>
///     Base type for every piece of content flowing through the Simorgh.AI pipeline.
/// </summary>
public abstract class AiContent
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyMetadata =
        new Dictionary<string, object?>();

    /// <summary>Free-form, provider-specific metadata attached to this content.</summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; protected set; } = EmptyMetadata;
}
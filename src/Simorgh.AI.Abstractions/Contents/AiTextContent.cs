namespace Simorgh.AI.Abstractions.Contents;

/// <summary>
///     Plain or lightly formatted textual content.
/// </summary>
/// <param name="text">The text itself.</param>
/// <param name="format">
///     Hint about the markup of <paramref name="text" />; e.g. <c>text</c>, <c>markdown</c>, <c>html</c>.
///     Defaults to <c>text</c>.
/// </param>
public sealed class AiTextContent(string text, string format = "text") : AiContent
{
    /// <summary>The text itself.</summary>
    public string Text { get; } = text;

    /// <summary>Hint about the markup of <see cref="Text" />; e.g. <c>text</c>, <c>markdown</c>, <c>html</c>.</summary>
    public string Format { get; } = format;
}
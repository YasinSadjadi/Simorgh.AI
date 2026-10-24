using Simorgh.AI.Abstractions.Contents;

namespace Simorgh.AI.Abstractions.Messages;

/// <summary>
///     A single message in a Simorgh.AI conversation, composed of one or more
///     <see cref="AiContent" /> parts.
/// </summary>
/// <param name="role">The author of the message.</param>
/// <param name="contents">The content parts of the message.</param>
public sealed class AiMessage(AiMessageRole role, IList<AiContent> contents)
{
    /// <summary>The author of the message.</summary>
    public AiMessageRole Role { get; } = role;

    /// <summary>The content parts of the message.</summary>
    public IList<AiContent> Contents { get; } = contents;

    /// <summary>Concatenation of all <see cref="AiTextContent" /> parts, or <c>null</c> when there are none.</summary>
    public string? Text
    {
        get
        {
            var texts = Contents.OfType<AiTextContent>().Select(c => c.Text).ToList();
            return texts.Count == 0 ? null : string.Join("\n", texts);
        }
    }

    /// <summary>Creates a message from a role and plain text.</summary>
    public static AiMessage FromText(AiMessageRole role, string text)
    {
        return new AiMessage(role, new List<AiContent> { new AiTextContent(text) });
    }
}
using System.Text.RegularExpressions;

namespace Simorgh.AI.Abstractions.Skills;

/// <summary>A named set of text instructions that an agent can discover and activate.</summary>
public sealed class SkillDto
{
    private static readonly Regex IdPattern = new(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z");

    /// <summary>Creates a skill with a stable identifier, discovery metadata, and instructions.</summary>
    public SkillDto(string id, string title, string whenToUse, IEnumerable<string> tags, string content)
    {
        if (id is null || id.Length > 64 || !IdPattern.IsMatch(id))
            throw new ArgumentException(
                "A skill id must be 1–64 lowercase letters or digits separated by single hyphens.", nameof(id));

        Id = id;
        Title = RequireText(title, nameof(title));
        WhenToUse = RequireText(whenToUse, nameof(whenToUse));
        Content = RequireText(content, nameof(content));
        if (tags is null) throw new ArgumentNullException(nameof(tags));
        Tags = Array.AsReadOnly(tags.Select(tag => RequireText(tag, nameof(tags))).ToArray());
    }

    /// <summary>The unique identifier used to discover and explicitly invoke the skill.</summary>
    public string Id { get; }

    /// <summary>The human-readable title.</summary>
    public string Title { get; }

    /// <summary>Guidance that tells the model when the skill applies.</summary>
    public string WhenToUse { get; }

    /// <summary>Search keywords for the skill. The collection may be empty.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>The instructions supplied to the model when the skill is activated.</summary>
    public string Content { get; }

    private static string RequireText(string value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty text value is required.", parameterName)
            : value;
    }
}
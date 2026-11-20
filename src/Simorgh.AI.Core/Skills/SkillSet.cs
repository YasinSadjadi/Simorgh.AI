using Simorgh.AI.Abstractions.Skills;

namespace Simorgh.AI.Core.Skills;

/// <summary>A snapshot of the skills available to an agent and its search configuration.</summary>
public sealed class SkillSet
{
    /// <summary>Reads <paramref name="skills" /> once and configures skill discovery.</summary>
    /// <param name="skills">The application-provided collection of skills.</param>
    /// <param name="searchProvider">The provider used for discovery. Defaults to full-text search.</param>
    /// <param name="maxResults">The maximum number of automatically discovered skills per run.</param>
    public SkillSet(IEnumerable<SkillDto> skills, ISkillSearchProvider? searchProvider = null, int maxResults = 5)
    {
        if (skills is null) throw new ArgumentNullException(nameof(skills));
        if (maxResults <= 0) throw new ArgumentOutOfRangeException(nameof(maxResults));

        var snapshot = new List<SkillDto>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in skills)
        {
            if (skill is null) throw new ArgumentException("The skill collection contains a null entry.", nameof(skills));
            if (!ids.Add(skill.Id))
                throw new ArgumentException($"A skill with id '{skill.Id}' is already present.", nameof(skills));

            snapshot.Add(skill);
        }

        Skills = snapshot.AsReadOnly();
        SearchProvider = searchProvider ?? new FullTextSkillSearchProvider();
        MaxResults = maxResults;
    }

    /// <summary>The skills available to the agent.</summary>
    public IReadOnlyList<SkillDto> Skills { get; }

    /// <summary>The provider used to rank skill candidates.</summary>
    public ISkillSearchProvider SearchProvider { get; }

    /// <summary>The maximum number of candidates shown to the model.</summary>
    public int MaxResults { get; }
}

namespace Simorgh.AI.Abstractions.Skills;

/// <summary>Finds relevant skills in a collection supplied by the application.</summary>
public interface ISkillSearchProvider
{
    /// <summary>Returns up to <paramref name="maxResults" /> skills in descending relevance order.</summary>
    /// <param name="skills">The available skills to search. Results must refer to this collection.</param>
    /// <param name="query">The text to match against the collection.</param>
    /// <param name="maxResults">A positive upper bound for the number of results.</param>
    /// <param name="cancellationToken">The token used to cancel the search.</param>
    Task<IReadOnlyList<SkillDto>> SearchAsync(
        IEnumerable<SkillDto> skills, string query, int maxResults, CancellationToken cancellationToken = default);
}

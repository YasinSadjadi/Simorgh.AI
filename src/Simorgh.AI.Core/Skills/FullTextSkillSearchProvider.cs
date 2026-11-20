using System.Text;
using System.Text.RegularExpressions;
using Simorgh.AI.Abstractions.Skills;

namespace Simorgh.AI.Core.Skills;

/// <summary>Ranks skills by whole-word matches across their title, tags, usage, and content.</summary>
public sealed class FullTextSkillSearchProvider : ISkillSearchProvider
{
    private static readonly Regex Words = new(@"[\p{L}\p{N}][\p{L}\p{N}\p{M}]*");

    /// <inheritdoc />
    public Task<IReadOnlyList<SkillDto>> SearchAsync(
        IEnumerable<SkillDto> skills, string query, int maxResults, CancellationToken cancellationToken = default)
    {
        if (skills is null) throw new ArgumentNullException(nameof(skills));
        if (query is null) throw new ArgumentNullException(nameof(query));
        if (maxResults <= 0) throw new ArgumentOutOfRangeException(nameof(maxResults));

        cancellationToken.ThrowIfCancellationRequested();
        var queryWords = GetWords(query, cancellationToken);
        if (queryWords.Count == 0)
            return Task.FromResult<IReadOnlyList<SkillDto>>([]);

        var matches = new List<(SkillDto Skill, int Coverage, int Score)>();
        foreach (var skill in skills)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matchedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var score = ScoreField(skill.Title, queryWords, matchedWords, cancellationToken) * 4
                        + ScoreField(string.Join(" ", skill.Tags), queryWords, matchedWords, cancellationToken) * 4
                        + ScoreField(skill.WhenToUse, queryWords, matchedWords, cancellationToken) * 3
                        + ScoreField(skill.Content, queryWords, matchedWords, cancellationToken);
            if (matchedWords.Count > 0)
                matches.Add((skill, matchedWords.Count, score));
        }

        IReadOnlyList<SkillDto> result = matches.OrderByDescending(match => match.Coverage)
            .ThenByDescending(match => match.Score)
            .ThenBy(match => match.Skill.Id, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(match => match.Skill)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(result);
    }

    private static int ScoreField(
        string text, HashSet<string> queryWords, HashSet<string> matchedWords, CancellationToken cancellationToken)
    {
        var fieldWords = GetWords(text, cancellationToken, queryWords);
        matchedWords.UnionWith(fieldWords);
        return fieldWords.Count;
    }

    private static HashSet<string> GetWords(
        string text, CancellationToken cancellationToken, HashSet<string>? filter = null)
    {
        // A zero-width non-joiner acts as a word boundary, just like a space.
        var normalized = text.Normalize(NormalizationForm.FormKC).Replace('ي', 'ی').Replace('ك', 'ک');
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var match = Words.Match(normalized); match.Success; match = match.NextMatch())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var word = match.Value;
            if (filter is null || filter.Contains(word))
                words.Add(word);
        }

        return words;
    }
}

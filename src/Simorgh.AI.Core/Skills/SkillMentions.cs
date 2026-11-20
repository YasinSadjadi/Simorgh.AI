using System.Text.RegularExpressions;

namespace Simorgh.AI.Core.Skills;

internal static class SkillMentions
{
    private static readonly Regex CodeLines = new(
        @"(?m)^(?: {0,3}(?<fence>`{3,}|~{3,})(?<rest>[^\r\n]*)|(?: {4}| {0,3}\t)[^\r\n]*)");

    private static readonly Regex CodeSpansOrMentions = new(
        @"(?<![`\\])(?<ticks>`+)(?!`)[\s\S]*?(?<!`)\k<ticks>(?!`)" +
        @"|(?<![^\s\p{Cf}(\[{])\$(?<id>[a-z0-9]+(?:-[a-z0-9]+)*)(?![\p{L}\p{M}\p{N}_/$-])");

    public static IEnumerable<string> Read(string text)
    {
        var start = 0;
        string? openFence = null;
        foreach (Match line in CodeLines.Matches(text))
        {
            var fence = line.Groups["fence"].Value;
            var rest = line.Groups["rest"].Value;
            if (openFence is not null)
            {
                if (fence.Length >= openFence.Length && fence[0] == openFence[0] && string.IsNullOrWhiteSpace(rest))
                {
                    openFence = null;
                    start = line.Index + line.Length;
                }

                continue;
            }

            if (fence.Length > 0 && fence[0] == '`' && rest.IndexOf('`') >= 0)
                continue;

            foreach (var id in ReadProse(text.Substring(start, line.Index - start)))
                yield return id;
            if (fence.Length > 0)
                openFence = fence;
            else
                start = line.Index + line.Length;
        }

        if (openFence is null)
        {
            foreach (var id in ReadProse(text.Substring(start)))
                yield return id;
        }
    }

    private static IEnumerable<string> ReadProse(string text)
    {
        foreach (Match match in CodeSpansOrMentions.Matches(text))
        {
            if (match.Groups["id"].Success)
                yield return match.Groups["id"].Value;
        }
    }
}

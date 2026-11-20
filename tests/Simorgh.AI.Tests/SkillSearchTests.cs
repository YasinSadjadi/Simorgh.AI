using Simorgh.AI.Abstractions.Skills;
using Simorgh.AI.Core.Skills;

namespace Simorgh.AI.Tests;

public sealed class SkillSearchTests
{
    private readonly FullTextSkillSearchProvider _provider = new();

    [Theory]
    [InlineData("invoice", "invoice", "usage", "tag", "instructions")]
    [InlineData("invoice", "title", "invoice", "tag", "instructions")]
    [InlineData("invoice", "title", "usage", "invoice", "instructions")]
    [InlineData("invoice", "title", "usage", "tag", "invoice")]
    [InlineData("پيگيري كارت بانکی", "پیگیری کارت‌بانکی", "usage", "tag", "instructions")]
    [InlineData("پيگيري كارت بانکی", "title", "پیگیری کارت‌بانکی", "tag", "instructions")]
    [InlineData("پيگيري كارت بانکی", "title", "usage", "پیگیری کارت‌بانکی", "instructions")]
    [InlineData("پيگيري كارت بانکی", "title", "usage", "tag", "پیگیری کارت‌بانکی")]
    public async Task Searches_whole_words_in_each_field(
        string query, string title, string whenToUse, string tag, string content)
    {
        var skill = new SkillDto("skill", title, whenToUse, [tag], content);

        Assert.Same(skill, Assert.Single(await _provider.SearchAsync([skill], query, 5)));
    }

    [Fact]
    public async Task Orders_by_distinct_word_coverage_then_field_priority_then_id()
    {
        SkillDto[] skills =
        [
            new("content", "Title", "Usage", [], "Invoice invoice invoice"),
            new("usage", "Title", "Invoice", [], "Instructions"),
            new("b-title", "Invoice", "Usage", [], "Instructions"),
            new("a-tags", "Title", "Usage", ["INVOICE"], "Instructions"),
            new("coverage", "Title", "Usage", [], "Invoice payments"),
            new("partial", "Invoices", "Usage", [], "Instructions"),
        ];

        var matches = await _provider.SearchAsync(skills, "INVOICE, payments invoice", 5);

        Assert.Equal(["coverage", "a-tags", "b-title", "usage", "content"], matches.Select(skill => skill.Id));
        Assert.Equal(["coverage", "a-tags"], (await _provider.SearchAsync(skills, "invoice payments", 2)).Select(skill => skill.Id));
    }

    [Theory]
    [InlineData(";;  ")]
    [InlineData("voice")]
    public async Task Returns_no_matches_for_empty_queries_or_substrings(string query)
    {
        var skill = new SkillDto("skill", "Invoice", "Usage", [], "Instructions");

        Assert.Empty(await _provider.SearchAsync([skill], query, 5));
    }

    [Fact]
    public void Skill_set_snapshots_queryable_input_and_rejects_duplicate_ids()
    {
        var tags = new List<string> { "initial" };
        var skill = new SkillDto("skill", "Title", "Usage", tags, "Instructions");
        var source = new List<SkillDto> { skill };
        var skillSet = new SkillSet(source.AsQueryable());
        source.Clear();
        tags.Clear();

        Assert.Same(skill, Assert.Single(skillSet.Skills));
        Assert.Equal("initial", Assert.Single(skill.Tags));
        Assert.Equal(5, skillSet.MaxResults);
        Assert.IsType<FullTextSkillSearchProvider>(skillSet.SearchProvider);
        Assert.Throws<ArgumentException>(() => new SkillSet([skill, skill]));
    }
}

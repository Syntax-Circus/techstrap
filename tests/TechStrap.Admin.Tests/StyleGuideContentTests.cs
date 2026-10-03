using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>What the Development style guide must show (docs/PHASE-02 task P02-T07). Each component task adds its section here.</summary>
public sealed class StyleGuideContentTests : IAsyncLifetime
{
    private AdminFactory _factory = default!;
    private IDocument _page = default!;

    public async ValueTask InitializeAsync()
    {
        _factory = new AdminFactory("Development");
        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/_styleguide", TestContext.Current.CancellationToken);
        _page = await new HtmlParser().ParseDocumentAsync(html, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private IElement Section(string headingId) =>
        _page.QuerySelector($"section[aria-labelledby='{headingId}']") ?? throw new InvalidOperationException($"No section labelled by #{headingId}");

    [Theory]
    [InlineData("sg-type")]
    [InlineData("sg-palette")]
    [InlineData("sg-buttons")]
    [InlineData("sg-forms")]
    [InlineData("sg-tables")]
    [InlineData("sg-alerts")]
    [InlineData("sg-states")]
    [InlineData("sg-stamps")]
    public void Section_is_present(string id)
    {
        Section(id).QuerySelector($"h2#{id}").ShouldNotBeNull(id);
    }

    [Fact]
    public void All_five_statuses_plus_spam_show_in_both_variants_and_the_four_priorities_show()
    {
        foreach (var status in new[] { "new", "open", "pending", "solved", "closed", "spam" })
        {
            Section("sg-stamps").QuerySelectorAll($".ts-stamp--queue.ts-stamp--{status}").Length.ShouldBe(1, $"queue {status}");
            Section("sg-stamps").QuerySelectorAll($".ts-stamp--ticket.ts-stamp--{status}").Length.ShouldBe(1, $"ticket {status}");
        }

        foreach (var level in new[] { "urgent", "high", "normal", "low" })
        {
            Section("sg-stamps").QuerySelectorAll($".ts-priority--{level}").Length.ShouldBe(1, level);
        }
    }

    [Fact]
    public void The_palette_lists_every_brand_token_with_a_swatch()
    {
        var shown = Section("sg-palette").QuerySelectorAll("code").Select(c => c.TextContent.TrimStart('-')).ToHashSet();

        foreach (var token in BrandTokenTable.Read())
        {
            shown.ShouldContain(token.Name);
        }
    }
}

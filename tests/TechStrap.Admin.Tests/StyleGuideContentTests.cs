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
        _page.QuerySelector($"section[aria-labelledby='{headingId}']") ?? throw new InvalidOperationException($"No section labeled by #{headingId}");

    [Theory]
    [InlineData("sg-type")]
    [InlineData("sg-palette")]
    [InlineData("sg-buttons")]
    [InlineData("sg-forms")]
    [InlineData("sg-tables")]
    [InlineData("sg-alerts")]
    [InlineData("sg-states")]
    [InlineData("sg-stamps")]
    [InlineData("sg-tints")]
    [InlineData("sg-keys")]
    [InlineData("sg-windows")]
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

    [Fact]
    public void The_three_tinted_entries_and_the_legend_show()
    {
        var section = Section("sg-tints");

        section.QuerySelectorAll(".ts-entry--customer").Length.ShouldBe(1);
        section.QuerySelectorAll(".ts-entry--public").Length.ShouldBe(1);
        section.QuerySelectorAll(".ts-entry--note").Length.ShouldBe(1);
        section.QuerySelectorAll(".ts-legend").Length.ShouldBe(1);
        section.QuerySelector(".ts-entry-label")!.TextContent.ShouldBe("INTERNAL NOTE");
    }

    [Fact]
    public void Every_keyboard_convention_of_BRAND_md_has_a_keycap_row()
    {
        var keys = Section("sg-keys").QuerySelectorAll("kbd").Select(k => k.TextContent).ToHashSet();

        foreach (var key in new[] { "j", "k", "Enter", "/", "r", "n", "e", "Esc", "Ctrl", "Cmd", "K" })
        {
            keys.ShouldContain(key);
        }
    }

    [Fact]
    public void The_three_brand_moment_windows_show_with_their_titles_and_one_action_each()
    {
        var windows = Section("sg-windows").QuerySelectorAll(".ts-window");

        windows.Select(w => w.QuerySelector(".ts-window-title")!.TextContent)
            .ShouldBe(["queue.exe — 0 items", "techstrap — sign in", "ERROR 404 — not found"]);
        foreach (var window in windows)
        {
            window.QuerySelectorAll(".ts-window-button, .ts-window-link").Length.ShouldBe(1, window.GetAttribute("aria-label"));
        }
    }

    [Fact]
    public void The_logo_wordmark_and_head_mark_are_shown_from_the_SVG_files()
    {
        var sources = Section("sg-windows").QuerySelectorAll(".ts-sg-logos img").Select(i => i.GetAttribute("src")).ToList();

        sources.ShouldBe(["brand/logo.svg", "brand/mark.svg", "brand/wordmark.svg"]);
    }
}

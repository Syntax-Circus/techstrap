using Bunit;
using TechStrap.Portal.Components.Kb;
using TechStrap.Portal.Components.Ui;

namespace TechStrap.Portal.Tests.Components;

/// <summary>
/// The shared help-centre components on their own (P09-T12, T13): what each renders for its parameters. Review Focus 1: a title, a summary, a crumb label and a search text are plain text, so a value that looks like markup
/// is shown as text and never becomes an element.
/// </summary>
public sealed class KbComponentTests : BunitContext
{
    [Fact]
    public void An_article_card_is_a_heading_link_with_an_optional_summary_and_meta_line()
    {
        var cut = Render<KbArticleCard>(parameters => parameters
            .Add(card => card.Href, "/p/paperplane/kb/accounts/reset-password")
            .Add(card => card.Title, "Reset your password")
            .Add(card => card.Summary, "How to reset it")
            .Add(card => card.Meta, "Updated 5 Oct 2026"));

        cut.Find("article.ts-kb-card h2 a").GetAttribute("href").ShouldBe("/p/paperplane/kb/accounts/reset-password");
        cut.Find("article.ts-kb-card h2 a").TextContent.ShouldBe("Reset your password");
        cut.Find(".ts-kb-summary").TextContent.ShouldBe("How to reset it");
        cut.Find(".ts-kb-meta").TextContent.ShouldBe("Updated 5 Oct 2026");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_card_without_a_summary_or_meta_has_no_empty_paragraph(string? summary)
    {
        var cut = Render<KbArticleCard>(parameters => parameters
            .Add(card => card.Href, "/x")
            .Add(card => card.Title, "Title")
            .Add(card => card.Summary, summary)
            .Add(card => card.Meta, summary));

        cut.FindAll("p").Count.ShouldBe(0);
    }

    [Fact]
    public void A_title_summary_and_meta_that_look_like_markup_are_shown_as_text()
    {
        var cut = Render<KbArticleCard>(parameters => parameters
            .Add(card => card.Href, "/x")
            .Add(card => card.Title, "<script>alert(1)</script>")
            .Add(card => card.Summary, "<img src=x onerror=alert(1)>")
            .Add(card => card.Meta, "<b>bold</b>"));

        cut.FindAll("script, img, b").Count.ShouldBe(0);
        cut.Find("h2 a").TextContent.ShouldBe("<script>alert(1)</script>");
        cut.Find(".ts-kb-summary").TextContent.ShouldBe("<img src=x onerror=alert(1)>");
        cut.Markup.ShouldContain("&lt;script&gt;");
    }

    [Fact]
    public void A_trail_links_every_step_but_the_last_which_is_the_current_page()
    {
        var cut = Render<KbBreadcrumbs>(parameters => parameters.Add(trail => trail.Crumbs, new[]
        {
            new KbCrumb("Paperplane", "/p/paperplane"),
            new KbCrumb("Help centre", "/p/paperplane/kb"),
            new KbCrumb("Accounts"),
        }));

        cut.Find("nav.ts-breadcrumbs").GetAttribute("aria-label").ShouldBe("Breadcrumb");
        cut.FindAll("nav.ts-breadcrumbs ol > li").Select(li => li.TextContent.Trim()).ShouldBe(["Paperplane", "Help centre", "Accounts"]);
        cut.FindAll("nav.ts-breadcrumbs a").Select(a => a.GetAttribute("href")).ShouldBe(["/p/paperplane", "/p/paperplane/kb"]);
        var last = cut.FindAll("li").Last();
        last.GetAttribute("aria-current").ShouldBe("page");
        last.QuerySelector("a").ShouldBeNull();
    }

    [Fact]
    public void A_crumb_label_that_looks_like_markup_is_shown_as_text_and_an_empty_trail_renders_nothing()
    {
        var hostile = Render<KbBreadcrumbs>(parameters => parameters.Add(trail => trail.Crumbs, new[] { new KbCrumb("<b>Cat</b>", "/x"), new KbCrumb("<i>Page</i>") }));
        var empty = Render<KbBreadcrumbs>(parameters => parameters.Add(trail => trail.Crumbs, Array.Empty<KbCrumb>()));

        hostile.FindAll("b, i").Count.ShouldBe(0);
        hostile.Find("a").TextContent.ShouldBe("<b>Cat</b>");
        empty.Markup.Trim().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(1, 10, 10)]
    public void A_list_of_one_page_or_none_has_no_pager(int page, int pageSize, int total)
    {
        var cut = Render<Pager>(parameters => parameters.Add(pager => pager.Page, page).Add(pager => pager.PageSize, pageSize).Add(pager => pager.TotalCount, total).Add(pager => pager.HrefFor, n => $"/x?page={n}"));

        cut.Markup.Trim().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, 11, new string[] { "/x?page=2" }, "Page 1 of 2")]
    [InlineData(2, 11, new string[] { "/x?page=1" }, "Page 2 of 2")]
    [InlineData(2, 25, new string[] { "/x?page=1", "/x?page=3" }, "Page 2 of 3")]
    [InlineData(3, 25, new string[] { "/x?page=2" }, "Page 3 of 3")]
    [InlineData(1, 25, new string[] { "/x?page=2" }, "Page 1 of 3")]
    public void A_pager_links_to_the_previous_and_the_next_page_only_where_there_is_one(int page, int total, string[] links, string state)
    {
        var cut = Render<Pager>(parameters => parameters.Add(pager => pager.Page, page).Add(pager => pager.PageSize, 10).Add(pager => pager.TotalCount, total).Add(pager => pager.HrefFor, n => $"/x?page={n}"));

        cut.FindAll("nav.ts-pager a").Select(a => a.GetAttribute("href")).ShouldBe(links);
        cut.Find(".ts-pager-state").TextContent.ShouldBe(state);
        cut.Find("nav.ts-pager").GetAttribute("aria-label").ShouldBe("Pages");
        cut.FindAll("a[rel=prev]").Count.ShouldBe(page > 1 ? 1 : 0);
        cut.FindAll("a[rel=next]").Count.ShouldBe(page < (total + 9) / 10 ? 1 : 0);
    }

    [Fact]
    public void A_state_message_has_a_heading_and_its_own_content_and_is_a_status_not_an_alert()
    {
        var cut = Render<StateMessage>(parameters => parameters.Add(state => state.Heading, "No articles found").AddChildContent("<p>Try again.</p>"));

        cut.Find("section.ts-state").GetAttribute("role").ShouldBe("status");
        cut.Find("h2").TextContent.ShouldBe("No articles found");
        cut.Find("p").TextContent.ShouldBe("Try again.");
    }

    [Fact]
    public void The_search_box_is_a_labelled_get_form_and_puts_the_text_back_as_an_encoded_value()
    {
        var cut = Render<KbSearchBox>(parameters => parameters.Add(box => box.ProductKey, "paperplane").Add(box => box.Query, "\"><script>alert(1)</script>"));

        var form = cut.Find("form[role=search]");
        form.GetAttribute("method").ShouldBe("get");
        form.GetAttribute("action").ShouldBe("/p/paperplane/kb/search");
        cut.Find("label[for=kb-search]").TextContent.ShouldBe("Search help articles");
        var input = cut.Find("input#kb-search");
        input.GetAttribute("name").ShouldBe("q");
        input.GetAttribute("maxlength").ShouldBe("200");
        input.GetAttribute("value").ShouldBe("\"><script>alert(1)</script>");
        cut.FindAll("script").Count.ShouldBe(0);
        cut.Find("button[type=submit]").TextContent.ShouldBe("Search");
    }
}

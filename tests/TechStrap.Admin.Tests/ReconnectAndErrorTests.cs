using TechStrap.Tests.Shared.AdminHost;
using System.Net;
using AngleSharp.Html.Parser;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// Admin is Blazor Server, so a lost circuit is a normal event. The SyntaxCircus.Blazor.Components reconnect dialog is mounted once in
/// App.razor, styled with brand tokens, with plain copy (no humour on a blocking error: BRAND.md section 3).
/// </summary>
public sealed class ReconnectAndErrorTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public async Task Every_page_mounts_exactly_one_styled_reconnect_dialog_with_plain_copy()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);
        var page = await new HtmlParser().ParseDocumentAsync(html, TestContext.Current.CancellationToken);

        var dialogs = page.QuerySelectorAll("dialog#components-reconnect-modal");
        dialogs.Length.ShouldBe(1);
        dialogs[0].ClassList.ShouldContain("ts-reconnect");
        dialogs[0].TextContent.ShouldContain("Connection lost. Reconnecting");
        dialogs[0].TextContent.ShouldContain("Your changes are still here");
        dialogs[0].QuerySelectorAll("[data-reconnect-action=retry]").Length.ShouldBe(1);
    }

    [Fact]
    public async Task The_reconnect_script_of_the_package_is_served()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/_content/SyntaxCircus.Blazor.Components/Components/Feedback/ReconnectModal.razor.js", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public void The_dialog_and_the_error_view_use_brand_tokens_and_a_scrim_backdrop()
    {
        Css.Declarations(".ts-reconnect")["background"].ShouldBe("var(--sheet)");
        Css.Declarations(".ts-reconnect")["border"].ShouldBe("2px solid var(--ink)");
        Css.Declarations(".ts-reconnect::backdrop")["background"].ShouldBe("var(--scrim)");
        Css.Declarations(".ts-error")["background"].ShouldBe("var(--sheet)");
        Css.Declarations(".ts-error")["border"].ShouldBe("2px solid var(--st-spam)");
    }

    [Fact]
    public async Task The_style_guide_previews_every_reconnect_state_and_the_error_view_without_a_second_dialog()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/_styleguide", TestContext.Current.CancellationToken);
        var page = await new HtmlParser().ParseDocumentAsync(html, TestContext.Current.CancellationToken);

        var section = page.QuerySelector("section[aria-labelledby='sg-reconnect']");
        section.ShouldNotBeNull();
        var states = section.QuerySelectorAll(".ts-reconnect--preview [data-reconnect-state]").Select(s => s.GetAttribute("data-reconnect-state")).ToList();
        states.ShouldBe(["first", "retrying", "failed", "paused", "resume-failed"]);
        section.QuerySelectorAll("section.ts-error[role=alert]").Length.ShouldBe(1);
        page.QuerySelectorAll("#components-reconnect-modal").Length.ShouldBe(1, "only the real dialog may carry the id");
    }
}

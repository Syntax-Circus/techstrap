using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Tests.Components;

namespace TechStrap.Admin.Tests.Auth;

/// <summary>A signed-in user the router refuses is told so; sending them to the sign-in page would loop with a single-sign-on provider.</summary>
public sealed class RedirectToSignInTests : BunitContext
{
    public RedirectToSignInTests() => this.AddAgentShell();

    [Fact]
    public void A_signed_in_user_sees_the_no_access_page_and_is_not_redirected()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var before = navigation.Uri;

        var cut = Render<RedirectToSignIn>(p => p.SignedIn());

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        navigation.Uri.ShouldBe(before);
    }

    // Carried ruling: refused one page is not refused the app, so the copy says "this page" and offers the way back to the queue.
    [Fact]
    public void A_signed_in_user_refused_one_page_gets_page_level_copy_and_a_link_to_the_queue()
    {
        var cut = Render<RedirectToSignIn>(p => p.SignedIn());

        cut.Find("section.ts-no-access h1").TextContent.ShouldBe("You don't have access to this page.");
        cut.Markup.ShouldNotContain("access to TechStrap");
        var link = cut.Find("section.ts-no-access a");
        link.GetAttribute("href").ShouldBe("/");
        link.TextContent.ShouldBe("Back to the queue");
    }

    [Fact]
    public void An_anonymous_visitor_is_sent_to_the_landing_page_with_a_local_return_url()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();

        Render<RedirectToSignIn>(p => p.SignedIn(signedIn: false));

        navigation.Uri.ShouldContain("/signin?returnUrl=");
    }
}

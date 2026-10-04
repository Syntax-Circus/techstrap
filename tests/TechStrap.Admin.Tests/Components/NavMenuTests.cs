using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>Review Focus 1, by nav: a plain agent never sees an admin link and never makes the admin-only badge call; an admin sees the five admin links and the failed-email count.</summary>
public sealed class NavMenuTests : AdminComponentTest
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private IAgentsClient Setup(string role)
    {
        var agents = this.AddAgentShell(role);
        return agents;
    }

    private IDeadLettersClient DeadLetters => Services.GetRequiredService<IDeadLettersClient>();

    private AgentSession Session => Services.GetRequiredService<AgentSession>();

    private IRenderedComponent<NavMenu> RenderRail() => Render<NavMenu>(p => p.SignedIn());

    [Fact]
    public async Task A_plain_agent_sees_the_queue_and_my_settings_and_no_admin_link()
    {
        Setup(AgentRoles.Agent);
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderRail();

        cut.FindAll("a.ts-rail-link").Select(a => (a.GetAttribute("href"), a.TextContent.Trim())).ShouldBe(
            [("/queue", "Queue"), ("/account/notifications", "My settings")]);
        cut.FindAll("a[href^='/settings']").ShouldBeEmpty();
        cut.FindAll("a[href^='/ops']").ShouldBeEmpty();
        cut.FindAll(".ts-rail-group").ShouldBeEmpty();
        cut.FindAll(".ts-rail-role").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_plain_agent_never_makes_the_admin_only_badge_call()
    {
        Setup(AgentRoles.Agent);
        await Session.EnsureLoadedAsync(Ct);

        RenderRail();
        await Task.Yield();

        await DeadLetters.DidNotReceive().CountAsync(Arg.Any<CancellationToken>());
        Services.GetRequiredService<FailedEmailCounter>().Count.ShouldBeNull();
    }

    [Fact]
    public async Task An_admin_sees_the_five_admin_links_in_order_and_my_settings()
    {
        Setup(AgentRoles.Admin);
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderRail();

        cut.FindAll("a.ts-rail-link").Select(a => (a.GetAttribute("href"), a.TextContent.Trim())).ShouldBe(
        [
            ("/queue", "Queue"),
            ("/settings/products", "Products"),
            ("/settings/agents", "Agents"),
            ("/settings/tags", "Tags"),
            ("/settings/audit", "Audit"),
            ("/ops/dead-letters", "Failed emails"),
            ("/account/notifications", "My settings"),
        ]);
        cut.Find(".ts-rail-group").GetAttribute("aria-label").ShouldBe("Admin");
        cut.Find(".ts-rail-role").TextContent.ShouldBe("Admin");
    }

    [Fact]
    public async Task An_admin_session_asks_for_the_failed_email_count_once_and_shows_it_as_a_badge()
    {
        Setup(AgentRoles.Admin);
        DeadLetters.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(3));
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderRail();

        cut.WaitForAssertion(() => cut.Find("a[href='/ops/dead-letters'] .ts-rail-badge").TextContent.ShouldBe("3"));
        cut.Find("a[href='/ops/dead-letters'] .ts-rail-badge").GetAttribute("aria-hidden").ShouldBe("true");
        cut.Find("a[href='/ops/dead-letters'] .visually-hidden").TextContent.ShouldBe("3 waiting");
        cut.Render();
        await DeadLetters.Received(1).CountAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_failed_emails_means_no_badge()
    {
        Setup(AgentRoles.Admin);
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderRail();

        cut.WaitForAssertion(() => DeadLetters.Received(1).CountAsync(Arg.Any<CancellationToken>()));
        cut.FindAll(".ts-rail-badge").ShouldBeEmpty();
        cut.Find("a[href='/ops/dead-letters']").TextContent.Trim().ShouldBe("Failed emails");
    }

    [Fact]
    public async Task A_failed_count_call_shows_no_badge_and_no_error()
    {
        Setup(AgentRoles.Admin);
        DeadLetters.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Failure(new ResultError(ApiErrorCodes.ApiUnavailable, "TechStrap could not reach the API.", ResultErrorKind.Failure)));
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderRail();

        cut.WaitForAssertion(() => DeadLetters.Received(1).CountAsync(Arg.Any<CancellationToken>()));
        cut.FindAll(".ts-rail-badge").ShouldBeEmpty();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("could not reach");
    }

    [Fact]
    public async Task The_badge_follows_the_counter_when_a_page_retries_or_discards_a_letter()
    {
        Setup(AgentRoles.Admin);
        DeadLetters.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(2));
        await Session.EnsureLoadedAsync(Ct);
        var cut = RenderRail();
        cut.WaitForAssertion(() => cut.Find(".ts-rail-badge").TextContent.ShouldBe("2"));

        await cut.InvokeAsync(() => Services.GetRequiredService<FailedEmailCounter>().Set(1));
        cut.Find(".ts-rail-badge").TextContent.ShouldBe("1");

        await cut.InvokeAsync(() => Services.GetRequiredService<FailedEmailCounter>().Set(0));
        cut.FindAll(".ts-rail-badge").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_reload_that_demotes_the_admin_removes_the_admin_links_from_the_rail()
    {
        var agents = Setup(AgentRoles.Admin);
        await Session.EnsureLoadedAsync(Ct);
        var cut = RenderRail();
        cut.FindAll("a[href^='/settings']").Count.ShouldBe(4);
        agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Agent, true, null, null)));

        await cut.InvokeAsync(() => Session.ReloadAsync(Ct));

        cut.FindAll("a[href^='/settings']").ShouldBeEmpty();
        cut.FindAll("a[href^='/ops']").ShouldBeEmpty();
        cut.Find("a[href='/account/notifications']").ShouldNotBeNull();
    }

    [Fact]
    public async Task The_rail_stays_complete_while_the_session_reloads()
    {
        var agents = Setup(AgentRoles.Admin);
        await Session.EnsureLoadedAsync(Ct);
        var cut = RenderRail();
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);

        var reload = cut.InvokeAsync(() => Session.ReloadAsync(Ct));
        cut.Render(); // force a re-render while the answer is pending

        cut.FindAll("a.ts-rail-link").Count.ShouldBe(7);
        gate.SetResult(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Admin, true, "Samantha", null)));
        await reload;
        cut.FindAll("a.ts-rail-link").Count.ShouldBe(7);
    }

    [Fact]
    public void Until_the_session_is_ready_the_rail_has_the_brand_alone_and_makes_no_call()
    {
        Setup(AgentRoles.Admin);

        var cut = RenderRail();

        cut.FindAll("a.ts-rail-link").ShouldBeEmpty();
        DeadLetters.DidNotReceive().CountAsync(Arg.Any<CancellationToken>());
    }

    // Review Focus 1, by keyboard: no key of the keyboard layer opens an admin page, for a plain agent or anyone else.
    [Theory]
    [InlineData("j")]
    [InlineData("k")]
    [InlineData("Enter")]
    [InlineData("/")]
    [InlineData("r")]
    [InlineData("n")]
    [InlineData("e")]
    [InlineData("u")]
    [InlineData("?")]
    [InlineData("Escape")]
    public async Task No_keyboard_shortcut_navigates_to_an_admin_page(string key)
    {
        this.AddAgentShell(AgentRoles.Agent);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/tickets/ACME-142");
        Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));

        await PressAsync(key);

        navigation.Uri.ShouldNotContain("/settings");
        navigation.Uri.ShouldNotContain("/ops");
    }
}

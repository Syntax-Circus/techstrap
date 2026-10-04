using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Pages;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 1: a plain agent never reaches an admin page by URL, and the guard neither denies nor flickers while the session loads or reloads. The role is the API's
/// answer in <see cref="AgentSession"/>; the content is not even built for a non-admin, so a component inside it never starts and never calls the API.
/// </summary>
public sealed class AdminOnlyTests : AdminComponentTest
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private int _contentStarted;

    public AdminOnlyTests() => this.AddAgentShell();

    /// <summary>Stands for the component a page puts inside the guard: it counts how many times it was started and renders a marker.</summary>
    private sealed class Probe : ComponentBase
    {
        [Parameter]
        public Action? Started { get; set; }

        protected override void OnInitialized() => Started?.Invoke();

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"secret\">admin content</p>");
    }

    private IRenderedComponent<AdminOnly> RenderGuard()
    {
        RenderFragment content = builder =>
        {
            builder.OpenComponent<Probe>(0);
            builder.AddAttribute(1, nameof(Probe.Started), (Action)(() => _contentStarted++));
            builder.CloseComponent();
        };
        return Render<AdminOnly>(p => p.Add(g => g.ChildContent, content));
    }

    private AgentSession Session => Services.GetRequiredService<AgentSession>();

    private IAgentsClient Agents => Services.GetRequiredService<IAgentsClient>();

    private static AgentDto Me(string role, string? publicName = null) =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Sam", "sam@orbitly.test", role, true, publicName, null);

    [Fact]
    public void Before_the_session_has_loaded_the_guard_shows_checking_and_neither_content_nor_a_refusal()
    {
        var cut = RenderGuard();

        cut.Find("p.ts-gate[role=status]").TextContent.ShouldBe("Checking your access...");
        cut.FindAll("#secret").ShouldBeEmpty();
        cut.FindAll(".ts-no-access").ShouldBeEmpty();
        _contentStarted.ShouldBe(0);
    }

    [Fact]
    public async Task A_plain_agent_gets_the_page_level_refusal_and_the_content_is_never_started()
    {
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderGuard();

        var refusal = cut.Find("section.ts-no-access");
        refusal.QuerySelector("h1")!.TextContent.ShouldBe(NoAccessCopy.PageTitle);
        refusal.QuerySelector("a")!.GetAttribute("href").ShouldBe("/");
        cut.FindAll("#secret").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("admin content");
        _contentStarted.ShouldBe(0);
    }

    [Fact]
    public async Task An_admin_sees_the_content_started_once()
    {
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Me(AgentRoles.Admin)));
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderGuard();

        cut.Find("#secret").TextContent.ShouldBe("admin content");
        cut.FindAll("section.ts-no-access").ShouldBeEmpty();
        _contentStarted.ShouldBe(1);
    }

    [Fact]
    public void The_guard_follows_the_session_when_it_loads_after_the_first_render()
    {
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Me(AgentRoles.Admin)));
        var cut = RenderGuard();
        cut.FindAll("#secret").ShouldBeEmpty();

        cut.InvokeAsync(() => Session.EnsureLoadedAsync(Ct));

        cut.WaitForAssertion(() => cut.Find("#secret").TextContent.ShouldBe("admin content"));
        _contentStarted.ShouldBe(1);
    }

    // The My settings save reloads the session. The guard must stay on the admin content while the answer is on its way, with no "Checking" frame and no restart.
    [Fact]
    public async Task A_session_reload_does_not_flicker_the_guard_or_restart_the_content()
    {
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Me(AgentRoles.Admin)));
        await Session.EnsureLoadedAsync(Ct);
        var cut = RenderGuard();
        var frames = new List<string>();
        Session.Changed += () => frames.Add(cut.Markup);
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);

        var reload = cut.InvokeAsync(() => Session.ReloadAsync(Ct));

        reload.IsCompleted.ShouldBeFalse();
        cut.Render(); // a re-render while the answer is pending must still show the content: a drop to NotLoaded would show "Checking"
        _contentStarted.ShouldBe(1);
        cut.Find("#secret").TextContent.ShouldBe("admin content");
        cut.FindAll(".ts-gate").ShouldBeEmpty();
        cut.FindAll("section.ts-no-access").ShouldBeEmpty();

        gate.SetResult(Result<AgentDto>.Success(Me(AgentRoles.Admin, "Samantha")));
        await reload;

        cut.Find("#secret").TextContent.ShouldBe("admin content");
        cut.FindAll(".ts-gate").ShouldBeEmpty();
        frames.ShouldAllBe(markup => markup.Contains("admin content") && !markup.Contains("ts-gate") && !markup.Contains("ts-no-access"));
        _contentStarted.ShouldBe(1);
    }

    [Fact]
    public async Task A_reload_that_demotes_the_agent_replaces_the_content_with_the_refusal()
    {
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Me(AgentRoles.Admin)), Result<AgentDto>.Success(Me(AgentRoles.Agent)));
        await Session.EnsureLoadedAsync(Ct);
        var cut = RenderGuard();
        cut.Find("#secret").ShouldNotBeNull();

        await cut.InvokeAsync(() => Session.ReloadAsync(Ct));

        cut.FindAll("#secret").ShouldBeEmpty();
        cut.Find("section.ts-no-access h1").TextContent.ShouldBe(NoAccessCopy.PageTitle);
    }

    [Fact]
    public async Task A_reload_that_deactivates_the_agent_removes_the_content_too()
    {
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Me(AgentRoles.Admin)));
        await Session.EnsureLoadedAsync(Ct);
        var cut = RenderGuard();
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Failure(new ResultError(ApiErrorCodes.AgentInactive, "Deactivated.", ResultErrorKind.Forbidden)));

        await cut.InvokeAsync(() => Session.ReloadAsync(Ct));

        // AgentGate shows the no-access screen for this state; the guard only withdraws the content and adds nothing of its own.
        cut.FindAll("#secret").ShouldBeEmpty();
        cut.FindAll(".ts-gate").ShouldBeEmpty();
        cut.FindAll("section.ts-no-access").ShouldBeEmpty();
        Session.State.ShouldBe(AgentSessionState.NoAccess);
    }
}

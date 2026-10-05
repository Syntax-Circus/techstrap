using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Tests.Components;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Auth;

/// <summary>
/// Review Focus 2, page half: a session that ends while the agent is working shows a banner over the same, still-mounted page, so an unsent reply or note
/// survives. A 401 on the first load stays the full session-expired page (07a), and an unexpected fault while loading degrades instead of ending the circuit.
/// </summary>
public sealed class AgentGateSessionExpiryTests : BunitContext
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private readonly IAgentsClient _agents;
    private readonly RecordingLoggerProvider _logs = new();

    public AgentGateSessionExpiryTests()
    {
        _agents = this.AddAgentShell();
        Services.AddLogging(logging => logging.AddProvider(_logs));
    }

    private AgentSession Session => Services.GetRequiredService<AgentSession>();

    private IRenderedComponent<AgentGate> RenderGate() => Render<AgentGate>(p => p
        .SignedIn()
        .Add(g => g.ChildContent, (RenderFragment)(builder =>
        {
            builder.OpenComponent<Draft>(0);
            builder.CloseComponent();
        })));

    /// <summary>Stands in for a composer: it holds what the agent typed in a field of its own, and counts how often it was created or removed.</summary>
    private sealed class Draft : ComponentBase, IDisposable
    {
        public static int Created;
        public static int Disposed;

        protected override void OnInitialized() => Interlocked.Increment(ref Created);

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "id", "draft");
            builder.AddContent(2, "unsent reply");
            builder.CloseElement();
        }

        public void Dispose() => Interlocked.Increment(ref Disposed);
    }

    private static void ResetDraftCounters()
    {
        Draft.Created = 0;
        Draft.Disposed = 0;
    }

    [Fact]
    public async Task A_session_that_expires_while_working_shows_the_banner_and_keeps_the_same_page_mounted()
    {
        ResetDraftCounters();
        var cut = RenderGate();
        await Session.EnsureLoadedAsync(Ct);
        cut.WaitForAssertion(() => cut.Find("#draft"));
        cut.FindAll(".ts-session-banner").ShouldBeEmpty();

        Services.GetRequiredService<SessionExpiry>().Report();

        cut.WaitForElement(".ts-session-banner");
        cut.Find("#draft").TextContent.ShouldBe("unsent reply");
        Draft.Created.ShouldBe(1, "the page component must be created once, not rebuilt when the banner appears");
        Draft.Disposed.ShouldBe(0, "tearing the page down would lose an unsent reply");
        cut.Find(".ts-session-banner").GetAttribute("role").ShouldBe("alert");
        cut.Find(".ts-session-banner").TextContent.ShouldContain(GateCopy.SessionLapsedTitle);
        cut.FindAll("section.ts-gate").ShouldBeEmpty("the full-page expired state is only for the first load");
    }

    [Fact]
    public async Task The_banner_links_to_sign_in_with_the_current_local_address()
    {
        var cut = RenderGate();
        await Session.EnsureLoadedAsync(Ct);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/queue/mine?status=open&search=a%20b");

        Services.GetRequiredService<SessionExpiry>().Report();

        var link = cut.WaitForElement(".ts-session-banner a");
        link.TextContent.ShouldBe(GateCopy.SignInAgain);
        link.GetAttribute("href").ShouldBe("/signin/start?returnUrl=%2Fqueue%2Fmine%3Fstatus%3Dopen%26search%3Da%2520b");
    }

    [Fact]
    public async Task The_banner_link_follows_the_agent_when_they_navigate()
    {
        var cut = RenderGate();
        await Session.EnsureLoadedAsync(Ct);
        Services.GetRequiredService<SessionExpiry>().Report();
        cut.WaitForElement(".ts-session-banner a");

        Services.GetRequiredService<NavigationManager>().NavigateTo("/tickets/ORB-7");

        cut.WaitForAssertion(() => cut.Find(".ts-session-banner a").GetAttribute("href").ShouldBe("/signin/start?returnUrl=%2Ftickets%2FORB-7"));
    }

    [Fact]
    public void A_401_on_the_first_load_is_the_full_page_with_no_content_and_no_banner()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Failure(new ResultError(ApiErrorCodes.Unauthenticated, "No.", ResultErrorKind.Unauthenticated)));
        ResetDraftCounters();

        var cut = RenderGate();

        cut.FindAll("#draft").ShouldBeEmpty();
        cut.FindAll(".ts-session-banner").ShouldBeEmpty();
        cut.Find("section.ts-gate h1").TextContent.ShouldBe(GateCopy.SessionExpiredTitle);
        cut.Find("form[action='/signin/start'] button").TextContent.ShouldBe(GateCopy.SignInAgain);
    }

    [Fact]
    public void An_unexpected_fault_while_loading_shows_unavailable_with_retry_and_logs_only_the_type()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromException<Result<AgentDto>>(new InvalidOperationException("secret-detail sam@orbitly.test")),
            Task.FromResult(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Agent, true, null, null))));
        ResetDraftCounters();

        var cut = RenderGate();

        cut.FindAll("#draft").ShouldBeEmpty();
        cut.Find("section.ts-gate h1").TextContent.ShouldBe(GateCopy.UnavailableTitle);
        _logs.Lines.ShouldContain(line => line.Contains("InvalidOperationException", StringComparison.Ordinal));
        _logs.Lines.ShouldNotContain(line => line.Contains("secret-detail", StringComparison.Ordinal) || line.Contains("sam@orbitly.test", StringComparison.Ordinal));

        cut.Find("section.ts-gate button").Click();

        cut.WaitForAssertion(() => cut.Find("#draft").TextContent.ShouldBe("unsent reply"));
    }
}

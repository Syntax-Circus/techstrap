using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// The connection indicator, and the one place the live client is started: after the first render in the circuit (never during prerendering) and only once the session is Ready, however often the
/// layout renders again. Each state has its text in a polite live region, and a failing client never reaches the page.
/// </summary>
public sealed class LiveConnectionIndicatorTests : AdminComponentTest
{
    [Theory]
    [InlineData(LiveConnectionState.Connected, LiveCopy.Connected)]
    [InlineData(LiveConnectionState.Connecting, LiveCopy.Connecting)]
    [InlineData(LiveConnectionState.Reconnecting, LiveCopy.Reconnecting)]
    [InlineData(LiveConnectionState.Disconnected, LiveCopy.Disconnected)]
    public void Each_state_is_drawn_with_its_text_in_a_polite_status_region(LiveConnectionState state, string text)
    {
        LiveClient.SetState(state);

        var cut = Render<LiveConnectionIndicator>();

        var region = cut.Find(".ts-live");
        region.GetAttribute("role").ShouldBe("status");
        region.GetAttribute("aria-live").ShouldBe("polite");
        region.GetAttribute("data-live-state").ShouldBe(state.ToString().ToLowerInvariant());
        region.TextContent.ShouldContain(text);
        region.TextContent.ShouldContain(LiveCopy.Label, Case.Sensitive);
    }

    [Fact]
    public void A_change_of_state_updates_the_same_live_region_so_it_is_announced()
    {
        var cut = Render<LiveConnectionIndicator>();
        cut.Find(".ts-live").TextContent.ShouldContain(LiveCopy.Connected);

        LiveClient.SetState(LiveConnectionState.Reconnecting);

        cut.WaitForAssertion(() => cut.Find(".ts-live").TextContent.ShouldContain(LiveCopy.Reconnecting));
        cut.FindAll("[aria-live]").Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_state_raised_on_another_thread_is_marshalled_to_the_renderer()
    {
        var cut = Render<LiveConnectionIndicator>();

        await Task.Run(() => LiveClient.SetState(LiveConnectionState.Disconnected), Xunit.TestContext.Current.CancellationToken);

        cut.WaitForAssertion(() => cut.Find(".ts-live").TextContent.ShouldContain(LiveCopy.Disconnected));
    }

    [Fact]
    public void The_client_is_started_once_from_the_first_render_and_not_again_on_later_renders()
    {
        var cut = Render<LiveConnectionIndicator>();
        LiveClient.StartCalls.ShouldBe(1);

        LiveClient.SetState(LiveConnectionState.Reconnecting);
        LiveClient.SetState(LiveConnectionState.Connected);
        cut.WaitForAssertion(() => cut.Find(".ts-live").TextContent.ShouldContain(LiveCopy.Connected));
        cut.Render();

        LiveClient.StartCalls.ShouldBe(1);
    }

    [Fact]
    public void Switched_off_nothing_is_drawn_and_nothing_is_started()
    {
        LiveClient.IsEnabled = false;

        var cut = Render<LiveConnectionIndicator>();

        cut.Markup.Trim().ShouldBeEmpty();
        LiveClient.StartCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Before_the_session_is_ready_nothing_is_drawn_and_nothing_is_started_then_it_starts_once()
    {
        var agents = Substitute.For<IAgentsClient>();
        var me = new AgentDto(AgentSessions.SamId, "Sam Ortiz", "sam@example.com", AgentRoles.Agent, IsActive: true, PublicDisplayName: null, LastSeenAt: null);
        var answer = new TaskCompletionSource<Result<AgentDto>>();
        agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(_ => answer.Task);
        var session = new AgentSession(agents);
        Services.AddSingleton(session);
        var cut = Render<LiveConnectionIndicator>();
        var load = session.EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken);

        cut.Markup.Trim().ShouldBeEmpty();
        LiveClient.StartCalls.ShouldBe(0);

        answer.SetResult(Result<AgentDto>.Success(me));
        await load;

        cut.WaitForAssertion(() => cut.Find(".ts-live").TextContent.ShouldContain(LiveCopy.Connected));
        LiveClient.StartCalls.ShouldBe(1);
    }

    [Fact]
    public async Task An_agent_without_access_never_starts_a_connection()
    {
        var agents = Substitute.For<IAgentsClient>();
        agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Failure(new ResultError("agent-inactive", "No access.", ResultErrorKind.Forbidden)));
        var session = new AgentSession(agents);
        Services.AddSingleton(session);
        var cut = Render<LiveConnectionIndicator>();

        await session.EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken);

        session.State.ShouldBe(AgentSessionState.NoAccess);
        cut.Markup.Trim().ShouldBeEmpty();
        LiveClient.StartCalls.ShouldBe(0);
    }

    [Fact]
    public void A_client_that_fails_to_start_never_breaks_the_page_and_only_the_type_is_logged()
    {
        var logs = new RecordingLoggerProvider();
        Services.AddLogging(logging => logging.AddProvider(logs));
        LiveClient.Failure = new InvalidOperationException("hub down");
        LiveClient.SetState(LiveConnectionState.Reconnecting);

        var cut = Render<LiveConnectionIndicator>();

        LiveClient.StartCalls.ShouldBe(1);
        cut.Find(".ts-live").TextContent.ShouldContain(LiveCopy.Reconnecting);
        logs.Lines.ShouldContain(line => line.Contains("InvalidOperationException", StringComparison.Ordinal));
        logs.Lines.ShouldNotContain(line => line.Contains("hub down", StringComparison.Ordinal));
    }

    [Fact]
    public void Disposal_unsubscribes_from_the_client_and_the_session()
    {
        var cut = Render<LiveConnectionIndicator>();
        LiveClient.HasSubscribers.ShouldBeTrue();

        cut.Instance.Dispose();

        LiveClient.HasSubscribers.ShouldBeFalse();
        Should.NotThrow(() => LiveClient.SetState(LiveConnectionState.Disconnected));
        cut.Markup.ShouldContain(LiveCopy.Connected, Case.Sensitive);
    }
}

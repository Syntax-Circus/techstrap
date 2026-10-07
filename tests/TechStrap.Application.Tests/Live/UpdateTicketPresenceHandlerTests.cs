using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Common;
using TechStrap.Application.Live;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Live;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Live;

public sealed class UpdateTicketPresenceHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly Guid TicketId = Guid.Parse("0197f2a0-0000-7000-8000-000000000001");
    private static readonly Guid OtherTicketId = Guid.Parse("0197f2a0-0000-7000-8000-000000000009");

    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly ITicketPresenceStore _store = Substitute.For<ITicketPresenceStore>();
    private readonly ITicketChangeBroadcaster _broadcaster = Substitute.For<ITicketChangeBroadcaster>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _sam;
    private readonly UpdateTicketPresenceHandler _handler;

    public UpdateTicketPresenceHandlerTests()
    {
        _sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(_sam);
        _tickets.GetStateAsync(TicketId, Arg.Any<CancellationToken>()).Returns(State(TicketId));
        _handler = new UpdateTicketPresenceHandler(_agents, _tickets, _store, _broadcaster, NullLogger<UpdateTicketPresenceHandler>.Instance);
    }

    private static TicketState State(Guid id) =>
        new(id, "ORB-1", TicketStatus.Open, TicketPriority.Normal, Guid.NewGuid(), null, false, [], DateTimeOffset.UnixEpoch, 1);

    private static TicketPresence Presence(Guid ticketId, params TicketViewer[] viewers) => new(ticketId, viewers);

    private TicketViewer Sam(TicketViewerState state = TicketViewerState.Viewing) => new(_sam.Id, "Sam", state);

    private static UpdateTicketPresenceRequest Request(string action, Guid? ticketId = null, bool isComposing = false, string subject = "sam", string connection = "conn-1") =>
        new(action, subject, connection, ticketId, isComposing);

    private Task<Result<TicketPresence>> HandleAsync(UpdateTicketPresenceRequest request) => _handler.HandleAsync(request, Ct);

    [Fact]
    public async Task Joining_an_unknown_ticket_is_not_found_and_touches_nothing()
    {
        _tickets.GetStateAsync(OtherTicketId, Arg.Any<CancellationToken>()).Returns((TicketState?)null);

        var result = await HandleAsync(Request(TicketPresenceActions.Join, OtherTicketId));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.NotFound);
        _store.ReceivedCalls().ShouldBeEmpty();
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishPresenceAsync(default!, Ct);
    }

    [Fact]
    public async Task Joining_as_an_agent_with_no_row_is_refused()
    {
        var result = await HandleAsync(Request(TicketPresenceActions.Join, TicketId, subject: "ghost"));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Forbidden);
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Joining_as_a_deactivated_agent_is_refused()
    {
        _sam.SetActive(false);

        var result = await HandleAsync(Request(TicketPresenceActions.Join, TicketId));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Forbidden);
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_join_that_changes_the_state_broadcasts_once_and_returns_the_snapshot()
    {
        var presence = Presence(TicketId, Sam());
        _store.Join("conn-1", TicketId, _sam.Id, "Sam").Returns(new PresenceChange(true, presence));

        var result = await HandleAsync(Request(TicketPresenceActions.Join, TicketId));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(presence);
        await _broadcaster.Received(1).PublishPresenceAsync(presence, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_join_that_changes_nothing_does_not_broadcast_but_still_returns_the_snapshot()
    {
        var presence = Presence(TicketId, Sam());
        _store.Join("conn-1", TicketId, _sam.Id, "Sam").Returns(new PresenceChange(false, presence));

        var result = await HandleAsync(Request(TicketPresenceActions.Join, TicketId));

        result.Value.ShouldBe(presence);
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishPresenceAsync(default!, Ct);
    }

    [Fact]
    public async Task The_name_shown_is_the_agents_own_name_and_falls_back_to_the_email()
    {
        var unnamed = Agent.Create("kim", null, "kim@example.com", AgentRole.Agent, _clock).Value;
        _agents.GetBySubjectAsync("kim", Arg.Any<CancellationToken>()).Returns(unnamed);
        _store.Join(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>()).Returns(new PresenceChange(false, Presence(TicketId)));

        await HandleAsync(Request(TicketPresenceActions.Join, TicketId, subject: "kim"));
        await HandleAsync(Request(TicketPresenceActions.Join, TicketId));

        _store.Received(1).Join("conn-1", TicketId, unnamed.Id, "kim@example.com");
        _store.Received(1).Join("conn-1", TicketId, _sam.Id, "Sam");
    }

    [Fact]
    public async Task The_public_display_name_is_never_used()
    {
        _sam.SetPublicDisplayName("Sammy from Support");
        _store.Join(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>()).Returns(new PresenceChange(false, Presence(TicketId)));

        await HandleAsync(Request(TicketPresenceActions.Join, TicketId));

        _store.Received(1).Join("conn-1", TicketId, _sam.Id, "Sam");
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_repositories_and_the_broadcaster()
    {
        using var source = new CancellationTokenSource();
        var presence = Presence(TicketId, Sam());
        _store.Join(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>()).Returns(new PresenceChange(true, presence));

        await _handler.HandleAsync(Request(TicketPresenceActions.Join, TicketId), source.Token);

        await _agents.Received(1).GetBySubjectAsync("sam", source.Token);
        await _tickets.Received(1).GetStateAsync(TicketId, source.Token);
        await _broadcaster.Received(1).PublishPresenceAsync(presence, source.Token);
    }

    [Fact]
    public async Task A_broadcaster_failure_does_not_fail_the_join()
    {
        var presence = Presence(TicketId, Sam());
        _store.Join(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>()).Returns(new PresenceChange(true, presence));
        _broadcaster.PublishPresenceAsync(Arg.Any<TicketPresence>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("hub is down"));

        var result = await HandleAsync(Request(TicketPresenceActions.Join, TicketId));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Leaving_broadcasts_only_when_the_state_changed()
    {
        var presence = Presence(TicketId);
        _store.Leave("conn-1", TicketId).Returns(new PresenceChange(true, presence), new PresenceChange(false, presence));

        (await HandleAsync(Request(TicketPresenceActions.Leave, TicketId))).IsSuccess.ShouldBeTrue();
        (await HandleAsync(Request(TicketPresenceActions.Leave, TicketId))).IsSuccess.ShouldBeTrue();

        await _broadcaster.Received(1).PublishPresenceAsync(presence, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leaving_needs_no_agent_lookup_so_a_deactivated_agent_can_always_clean_up()
    {
        _sam.SetActive(false);
        _store.Leave("conn-1", TicketId).Returns(new PresenceChange(true, Presence(TicketId)));

        var result = await HandleAsync(Request(TicketPresenceActions.Leave, TicketId));

        result.IsSuccess.ShouldBeTrue();
        await _agents.DidNotReceiveWithAnyArgs().GetBySubjectAsync(default!, Ct);
    }

    [Fact]
    public async Task Leaving_everything_broadcasts_one_update_per_ticket_that_changed_and_none_for_the_rest()
    {
        var first = Presence(TicketId);
        var second = Presence(OtherTicketId);
        _store.LeaveAll("conn-1").Returns([new PresenceChange(true, first), new PresenceChange(false, second)]);

        var result = await HandleAsync(Request(TicketPresenceActions.LeaveAll));

        result.IsSuccess.ShouldBeTrue();
        await _broadcaster.Received(1).PublishPresenceAsync(first, Arg.Any<CancellationToken>());
        await _broadcaster.DidNotReceive().PublishPresenceAsync(second, Arg.Any<CancellationToken>());
        await _agents.DidNotReceiveWithAnyArgs().GetBySubjectAsync(default!, Ct);
    }

    [Fact]
    public async Task One_failing_broadcast_does_not_stop_the_other_leaves_from_being_sent()
    {
        var first = Presence(TicketId);
        var second = Presence(OtherTicketId);
        _store.LeaveAll("conn-1").Returns([new PresenceChange(true, first), new PresenceChange(true, second)]);
        _broadcaster.PublishPresenceAsync(first, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("x"));

        var result = await HandleAsync(Request(TicketPresenceActions.LeaveAll));

        result.IsSuccess.ShouldBeTrue();
        await _broadcaster.Received(1).PublishPresenceAsync(second, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Composing_for_a_ticket_the_connection_never_joined_is_a_failure_and_broadcasts_nothing()
    {
        _store.SetComposing("conn-1", TicketId, true).Returns((PresenceChange?)null);

        var result = await HandleAsync(Request(TicketPresenceActions.SetComposing, TicketId, isComposing: true));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("presence-not-joined");
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishPresenceAsync(default!, Ct);
    }

    [Fact]
    public async Task Composing_broadcasts_on_a_change_and_not_on_a_repeat()
    {
        var presence = Presence(TicketId, Sam(TicketViewerState.Composing));
        _store.SetComposing("conn-1", TicketId, true).Returns(new PresenceChange(true, presence), new PresenceChange(false, presence));

        await HandleAsync(Request(TicketPresenceActions.SetComposing, TicketId, isComposing: true));
        await HandleAsync(Request(TicketPresenceActions.SetComposing, TicketId, isComposing: true));

        await _broadcaster.Received(1).PublishPresenceAsync(presence, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Composing_as_a_deactivated_agent_is_refused()
    {
        _sam.SetActive(false);

        var result = await HandleAsync(Request(TicketPresenceActions.SetComposing, TicketId, isComposing: true));

        result.IsFailure.ShouldBeTrue();
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Teleport", "conn-1", true)]
    [InlineData("", "conn-1", true)]
    [InlineData(TicketPresenceActions.Join, "", true)]
    [InlineData(TicketPresenceActions.Join, "   ", true)]
    [InlineData(TicketPresenceActions.Join, "conn-1", false)]
    [InlineData(TicketPresenceActions.Leave, "conn-1", false)]
    [InlineData(TicketPresenceActions.SetComposing, "conn-1", false)]
    public async Task A_request_with_a_bad_action_connection_or_missing_ticket_is_a_validation_failure(string action, string connection, bool hasTicket)
    {
        var result = await HandleAsync(new UpdateTicketPresenceRequest(action, "sam", connection, hasTicket ? TicketId : null, false));

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Validation);
        _store.ReceivedCalls().ShouldBeEmpty();
        await _agents.DidNotReceiveWithAnyArgs().GetBySubjectAsync(default!, Ct);
    }
}

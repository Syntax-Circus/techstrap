using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tags;
using TechStrap.Application.Tests.Support;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tags;

public sealed class DeleteTagRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITagRepository _tags = Substitute.For<ITagRepository>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Tag _tag;

    public DeleteTagRequestHandlerTests()
    {
        var admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(admin);
        _tag = Tag.Create("bug", "Bug", "#DC2626", _clock).Value;
        _tags.GetByIdAsync(_tag.Id, Arg.Any<CancellationToken>()).Returns(_tag);
    }

    private DeleteTagRequestHandler Handler() => new(_claims, _agents, _tags, _tickets, _events, UnitOfWorkSubstitute.Create(), _clock);

    private Ticket TicketCarryingTheTag(long sequence)
    {
        var ticket = Ticket.Create(
            TicketNumber.Create("ACME", sequence).Value, Guid.NewGuid(), Guid.NewGuid(), "Cannot sign in", TicketChannel.Web, null, false, _clock).Value;
        ticket.AddTag(_tag.Id, Actor.ForAgent(Guid.NewGuid()), _clock).IsSuccess.ShouldBeTrue();
        ticket.AcceptChanges();
        return ticket;
    }

    [Fact]
    public async Task An_unused_tag_is_deleted_and_audited()
    {
        _tickets.ListTicketIdsWithTagAsync(_tag.Id, Arg.Any<CancellationToken>()).Returns([]);

        (await Handler().HandleAsync(_tag.Id, force: false, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        _tags.Received(1).Remove(_tag);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.Type == AdminEventType.TagDeleted && e.PayloadJson == "{\"slug\":\"bug\",\"detachedTicketCount\":0}"));
    }

    [Fact]
    public async Task A_tag_in_use_is_a_conflict_that_states_how_many_tickets_carry_it()
    {
        _tickets.ListTicketIdsWithTagAsync(_tag.Id, Arg.Any<CancellationToken>()).Returns([Guid.CreateVersion7(), Guid.CreateVersion7()]);

        var result = await Handler().HandleAsync(_tag.Id, force: false, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("tag-in-use"),
            error => error.Message.ShouldContain("2 tickets"));
        _tags.DidNotReceive().Remove(Arg.Any<Tag>());
    }

    [Fact]
    public async Task An_unknown_tag_is_not_found()
    {
        (await Handler().HandleAsync(Guid.CreateVersion7(), force: true, TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task Force_detaches_the_tag_from_every_ticket_then_deletes_it()
    {
        var ticketA = TicketCarryingTheTag(1);
        var ticketB = TicketCarryingTheTag(2);
        _tickets.ListTicketIdsWithTagAsync(_tag.Id, Arg.Any<CancellationToken>()).Returns([ticketA.Id, ticketB.Id]);
        _tickets.GetByIdAsync(ticketA.Id, Arg.Any<CancellationToken>()).Returns(ticketA);
        _tickets.GetByIdAsync(ticketB.Id, Arg.Any<CancellationToken>()).Returns(ticketB);

        var result = await Handler().HandleAsync(_tag.Id, force: true, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        foreach (var ticket in new[] { ticketA, ticketB })
        {
            ticket.TagIds.ShouldNotContain(_tag.Id);
            ticket.PendingEvents.ShouldHaveSingleItem().Type.ShouldBe(TicketEventType.TagRemoved);
        }

        _tickets.Received(1).Update(ticketA);
        _tickets.Received(1).Update(ticketB);
        _tags.Received(1).Remove(_tag);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.Type == AdminEventType.TagDeleted && e.PayloadJson == "{\"slug\":\"bug\",\"detachedTicketCount\":2}"));
    }

    [Fact]
    public async Task A_deactivated_actor_is_refused_before_any_lookup_or_staging()
    {
        var inactive = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        inactive.SetActive(false);
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(inactive);

        var result = await Handler().HandleAsync(_tag.Id, force: true, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        _ = _tags.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _ = _tickets.DidNotReceive().ListTicketIdsWithTagAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _tags.DidNotReceive().Remove(Arg.Any<Tag>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task The_in_use_message_is_singular_for_one_ticket()
    {
        _tickets.ListTicketIdsWithTagAsync(_tag.Id, Arg.Any<CancellationToken>()).Returns([Guid.CreateVersion7()]);

        var result = await Handler().HandleAsync(_tag.Id, force: false, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Message.ShouldContain("on 1 ticket.");
    }
}

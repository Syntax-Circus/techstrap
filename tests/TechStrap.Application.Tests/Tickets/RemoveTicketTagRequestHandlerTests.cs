using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class RemoveTicketTagRequestHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketRepository _tickets;
    private List<(TicketEventType Type, string Payload)> _staged = [];

    private readonly ITagRepository _tags = Substitute.For<ITagRepository>();

    private Tag GivenTag()
    {
        var tag = Tag.Create("bug", "Bug", "#DC2626", _clock).Value;
        _tags.GetByIdAsync(tag.Id, Arg.Any<CancellationToken>()).Returns(tag);
        return tag;
    }

    public RemoveTicketTagRequestHandlerTests()
    {
        _tickets = TicketRepositorySubstitute.Create(t => _staged = [.. t.PendingEvents.Select(e => (e.Type, e.PayloadJson))]);
        var sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(sam);
    }

    private RemoveTicketTagRequestHandler Handler() => new(_claims, _agents, _tickets, _tags, UnitOfWorkSubstitute.Create(), _clock);

    private Ticket GivenTicket(TicketStatus status = TicketStatus.Open, uint version = 5)
    {
        var ticket = TicketBuilder.WithVersion(TicketBuilder.InStatus(status, _clock), version);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        _tickets.GetStateAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(_ =>
            new TicketState(ticket.Id, ticket.Number.ToString(), ticket.Status, ticket.Priority, ticket.ProductId, ticket.AssigneeId, ticket.IsSpam, [.. ticket.TagIds], ticket.LastActivityAt, version + 1));
        return ticket;
    }

    [Fact]
    public async Task Removing_a_tag_writes_TagRemoved()
    {
        var ticket = GivenTicket();
        var tag = GivenTag();
        ticket.AddTag(tag.Id, Actor.ForAgent(Guid.NewGuid()), _clock).IsSuccess.ShouldBeTrue();
        ticket.AcceptChanges();

        var result = await Handler().HandleAsync(ticket.Id, tag.Id, 5, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TagIds.ShouldBeEmpty();
        _staged.Select(e => e.Type).ShouldBe([TicketEventType.TagRemoved]);
        _tickets.Received(1).Update(ticket);
    }

    [Fact]
    public async Task Removing_an_absent_tag_is_200_with_no_event()
    {
        var ticket = GivenTicket();
        var tag = GivenTag();

        var result = await Handler().HandleAsync(ticket.Id, tag.Id, 5, Ct);

        result.IsSuccess.ShouldBeTrue();
        _staged.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_tag_is_404()
    {
        var ticket = GivenTicket();

        var result = await Handler().HandleAsync(ticket.Id, Guid.NewGuid(), 5, Ct);

        result.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task A_missing_row_version_on_remove_is_400()
    {
        var ticket = GivenTicket();
        var tag = GivenTag();

        var result = await Handler().HandleAsync(ticket.Id, tag.Id, null, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation), e => e.Code.ShouldBe("row-version-required"), e => e.Target.ShouldBe("rowVersion"));
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }
}

using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Content;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class AddInternalNoteRequestHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketRepository _tickets = TicketRepositorySubstitute.Create();
    private readonly IMarkdownRenderer _markdown = Substitute.For<IMarkdownRenderer>();
    private readonly IHtmlSanitizer _sanitizer = Substitute.For<IHtmlSanitizer>();
    private readonly Agent _sam;

    public AddInternalNoteRequestHandlerTests()
    {
        _sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(_sam);
        _markdown.ToHtml(Arg.Any<string>()).Returns(call => string.IsNullOrWhiteSpace(call.Arg<string>()) ? string.Empty : "<p>" + call.Arg<string>() + "</p>"); // like the real renderer
        _sanitizer.Sanitize(Arg.Any<string>()).Returns(call => call.Arg<string>());
    }

    private AddInternalNoteRequestHandler Handler() => new(_claims, _agents, _tickets, _markdown, _sanitizer, UnitOfWorkSubstitute.Create(), _clock);

    private Ticket GivenTicket(TicketStatus status)
    {
        var ticket = TicketBuilder.WithVersion(TicketBuilder.InStatus(status, _clock), 3);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        _tickets.GetStateAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(_ =>
            new TicketState(ticket.Id, ticket.Number.ToString(), ticket.Status, ticket.Priority, ticket.ProductId, ticket.AssigneeId, ticket.IsSpam, [], ticket.LastActivityAt, 4));
        return ticket;
    }

    [Fact]
    public async Task A_note_is_internal_never_emails_and_never_changes_status_or_first_response()
    {
        var ticket = GivenTicket(TicketStatus.Open);

        ticket.FirstResponseAt.ShouldBeNull();
        var result = await Handler().HandleAsync(ticket.Id, new AddInternalNoteRequest("Check **logs**", null), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Message.Visibility.ShouldBe("Internal");
        result.Value.Message.BodyHtml.ShouldBe("<p>Check **logs**</p>");
        result.Value.Message.AuthorId.ShouldBe(_sam.Id);
        result.Value.Ticket.RowVersion.ShouldBe(4u);
        ticket.Status.ShouldBe(TicketStatus.Open);
        ticket.FirstResponseAt.ShouldBeNull();
        _tickets.Received(1).Update(ticket);
    }

    [Fact]
    public async Task A_note_on_a_closed_ticket_is_409()
    {
        var ticket = GivenTicket(TicketStatus.Closed);

        var result = await Handler().HandleAsync(ticket.Id, new AddInternalNoteRequest("Late", null), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict),
            e => e.Code.ShouldBe("ticket-closed"));
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task An_over_long_note_is_rejected_before_markdown_runs()
    {
        var ticket = GivenTicket(TicketStatus.Open);

        var result = await Handler().HandleAsync(ticket.Id, new AddInternalNoteRequest(new string('a', DomainLimits.MessageBodyMaxLength + 1), null), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("body-too-long");
        _markdown.DidNotReceiveWithAnyArgs().ToHtml(default!);
        await _tickets.DidNotReceiveWithAnyArgs().GetByIdAsync(default, Ct);
    }

    [Fact]
    public async Task A_stale_optional_row_version_is_409_and_nothing_is_stored()
    {
        var ticket = GivenTicket(TicketStatus.Open);

        var result = await Handler().HandleAsync(ticket.Id, new AddInternalNoteRequest("Note", 2), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict),
            e => e.Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict));
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_or_blank_note_is_400_body_required(string? body)
    {
        var ticket = GivenTicket(TicketStatus.Open);

        var result = await Handler().HandleAsync(ticket.Id, new AddInternalNoteRequest(body, null), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation),
            e => e.Code.ShouldBe("body-required"));
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }
}

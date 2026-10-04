using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class MoveTicketProductRequestHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ITicketRepository _tickets;
    private List<(TicketEventType Type, string Payload)> _staged = [];

    public MoveTicketProductRequestHandlerTests()
    {
        _tickets = TicketRepositorySubstitute.Create(t => _staged = [.. t.PendingEvents.Select(e => (e.Type, e.PayloadJson))]);
        var sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(sam);
    }

    private MoveTicketProductRequestHandler Handler() => new(_claims, _agents, _tickets, _products, UnitOfWorkSubstitute.Create(), _clock);

    private Ticket GivenTicket(TicketStatus status = TicketStatus.Open, uint version = 5)
    {
        var ticket = TicketBuilder.WithVersion(TicketBuilder.InStatus(status, _clock), version);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        _tickets.GetStateAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(_ =>
            new TicketState(ticket.Id, ticket.Number.ToString(), ticket.Status, ticket.Priority, ticket.ProductId, ticket.AssigneeId, ticket.IsSpam, [], ticket.LastActivityAt, version + 1));
        return ticket;
    }

    private Product GivenProduct(bool active = true)
    {
        var product = Product.Create("paperplane", "Paperplane", "PPL", null, _clock).Value;
        product.SetActive(active);
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        return product;
    }

    [Fact]
    public async Task A_move_keeps_the_number_and_writes_ProductChanged()
    {
        var ticket = GivenTicket();
        var number = ticket.Number;
        var product = GivenProduct();

        var result = await Handler().HandleAsync(ticket.Id, new MoveTicketProductRequest(product.Id, 5), Ct);

        result.IsSuccess.ShouldBeTrue();
        ticket.ProductId.ShouldBe(product.Id);
        ticket.Number.ShouldBe(number);
        result.Value.RowVersion.ShouldBe(6u);
        _staged.Select(e => e.Type).ShouldBe([TicketEventType.ProductChanged]);
    }

    [Fact]
    public async Task An_unknown_product_is_404_an_inactive_one_is_400_and_a_missing_one_is_400()
    {
        var ticket = GivenTicket();
        var inactive = GivenProduct(active: false);

        var unknown = await Handler().HandleAsync(ticket.Id, new MoveTicketProductRequest(Guid.NewGuid(), 5), Ct);
        var off = await Handler().HandleAsync(ticket.Id, new MoveTicketProductRequest(inactive.Id, 5), Ct);
        var missing = await Handler().HandleAsync(ticket.Id, new MoveTicketProductRequest(null, 5), Ct);

        unknown.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.NotFound), e => e.Code.ShouldBe("product-not-found"));
        off.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation), e => e.Code.ShouldBe("product-inactive"), e => e.Target.ShouldBe("productId"));
        missing.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation), e => e.Code.ShouldBe("product-required"), e => e.Target.ShouldBe("productId"));
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task A_closed_ticket_is_409()
    {
        var ticket = GivenTicket(TicketStatus.Closed);
        var product = GivenProduct();

        var result = await Handler().HandleAsync(ticket.Id, new MoveTicketProductRequest(product.Id, 5), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("ticket-closed");
    }
}

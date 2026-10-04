using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Persistence;
using TechStrap.Application.Security;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets.Customer;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using SyntaxCircus.Common;

namespace TechStrap.Application.Tests.Tickets.Customer;

public sealed class GetCustomerTicketRequestHandlerTests
{
    private const string Raw = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly FakeTimeProvider _clock = new();
    private readonly IAccessTokenService _tokens = Substitute.For<IAccessTokenService>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly IRequesterRepository _requesters = Substitute.For<IRequesterRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly Agent _sam;
    private readonly Requester _ann;
    private readonly Product _product;
    private readonly Ticket _ticket;
    private readonly TicketAccessToken _token;
    private readonly Message _customerMessage;
    private readonly Message _agentMessage;
    private readonly Message _systemMessage;
    private readonly Attachment _attachment;

    public GetCustomerTicketRequestHandlerTests()
    {
        _sam = Agent.Create("sam", "Sam Hargreaves", "sam@example.com", AgentRole.Agent, _clock).Value;
        _ann = Requester.Create("ann@example.com", "Ann", null, _clock).Value;
        _product = Product.Create("orbitly", "Orbitly", "ORB", null, _clock).Value;
        _ticket = TicketBuilder.New(_clock, _product.Id, _ann.Id, 42);
        _token = TicketAccessToken.Issue(_ticket.Id, _ann.Id, "sha256:abc", _clock).Value;
        _customerMessage = Msg(AuthorType.Requester, _ann.Id, "<p>Help</p>", 0);
        _agentMessage = Msg(AuthorType.Agent, _sam.Id, "<p>On it</p>", 1);
        _systemMessage = Msg(AuthorType.System, null, "<p>Note</p>", 2);
        _attachment = Attachment.Restore(Guid.NewGuid(), _ticket.Id, _agentMessage.Id, "log.txt", "text/plain", 10, "key", _clock.GetUtcNow());

        _tokens.Hash(Raw).Returns("sha256:abc");
        _tickets.GetAccessTokenByHashAsync("sha256:abc", Arg.Any<CancellationToken>()).Returns(_token);
        _tickets.GetByIdAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns(_ticket);
        _requesters.GetByIdAsync(_ann.Id, Arg.Any<CancellationToken>()).Returns(_ann);
        _tickets.GetMessagesAsync(_ticket.Id, true, Arg.Any<CancellationToken>()).Returns([_customerMessage, _agentMessage, _systemMessage]);
        _tickets.GetAttachmentsAsync(_ticket.Id, true, Arg.Any<CancellationToken>()).Returns([_attachment]);
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(_product);
        _agents.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([_sam]);
    }

    private GetCustomerTicketRequestHandler Handler(IUnitOfWork? unitOfWork = null) =>
        new(_tokens, _tickets, _requesters, _agents, _products, unitOfWork ?? UnitOfWorkSubstitute.Create(), _clock);

    private Message Msg(AuthorType type, Guid? authorId, string body, int minute) =>
        Message.Restore(Guid.NewGuid(), _ticket.Id, type, authorId, MessageVisibility.Public, body, null, null, _clock.GetUtcNow().AddMinutes(minute));

    [Fact]
    public async Task A_valid_token_returns_only_public_messages_with_resolved_agent_names()
    {
        var result = await Handler().HandleAsync(Raw, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Number.ShouldBe("TS-42");
        result.Value.Subject.ShouldBe("Cannot sign in");
        result.Value.Status.ShouldBe("New");
        result.Value.Messages.Select(m => m.AuthorDisplayName).ShouldBe([null, "Sam from Orbitly Support", null]);
        result.Value.Messages.Select(m => m.AuthorType).ShouldBe(["Requester", "Agent", "System"]);
        result.Value.Messages[1].BodyHtml.ShouldBe("<p>On it</p>");
        result.Value.Messages[1].Attachments.ShouldHaveSingleItem().Id.ShouldBe(_attachment.Id);
        result.Value.Messages[0].Attachments.ShouldBeEmpty();
        await _tickets.Received(1).GetMessagesAsync(_ticket.Id, true, Arg.Any<CancellationToken>());
        await _tickets.Received(1).GetAttachmentsAsync(_ticket.Id, true, Arg.Any<CancellationToken>());
        await _tickets.DidNotReceive().GetMessagesAsync(_ticket.Id, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_view_slides_the_token_expiry()
    {
        _clock.Advance(TimeSpan.FromDays(30));
        var before = _token.ExpiresAt;

        var result = await Handler().HandleAsync(Raw, Ct);

        result.IsSuccess.ShouldBeTrue();
        _token.ExpiresAt.ShouldBe(before + TimeSpan.FromDays(30));
        _token.LastUsedAt.ShouldBe(_clock.GetUtcNow());
        _tickets.Received(1).UpdateAccessToken(_token);
    }

    [Fact]
    public async Task A_commit_conflict_still_returns_the_view_but_another_failure_is_not_found()
    {
        var raced = await Handler(UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ConcurrencyConflict)))
            .HandleAsync(Raw, Ct);
        raced.IsSuccess.ShouldBeTrue();

        var other = await Handler(UnitOfWorkSubstitute.Create(
            Result.Failure(new ResultError("boom", "x", ResultErrorKind.Validation)))).HandleAsync(Raw, Ct);
        other.IsFailure.ShouldBeTrue();
        other.Errors.Single().Code.ShouldBe(CustomerErrors.NotFoundCode);
    }

    [Fact]
    public async Task Any_access_failure_is_the_uniform_not_found()
    {
        var errors = new List<ResultError>();
        async Task FailAsync(string? raw)
        {
            var result = await Handler().HandleAsync(raw, Ct);
            result.IsFailure.ShouldBeTrue();
            errors.Add(result.Errors.Single());
            _tickets.DidNotReceiveWithAnyArgs().UpdateAccessToken(default!);
        }

        foreach (var raw in new string?[] { null, "", "garbage", new string('x', 200) })
        {
            await FailAsync(raw);
        }

        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns((Product?)null);
        await FailAsync(Raw);
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(_product);

        _tickets.GetByIdAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns((Ticket?)null);
        await FailAsync(Raw);
        _tickets.GetByIdAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns(_ticket);

        _ann.Erase(_clock);
        await FailAsync(Raw);

        var revoked = TicketAccessToken.Issue(_ticket.Id, _ann.Id, "sha256:abc", _clock).Value;
        revoked.Revoke(_clock);
        _tickets.GetAccessTokenByHashAsync("sha256:abc", Arg.Any<CancellationToken>()).Returns(revoked);
        await FailAsync(Raw);

        _clock.Advance(TicketAccessToken.Lifetime + TimeSpan.FromDays(1));
        _tickets.GetAccessTokenByHashAsync("sha256:abc", Arg.Any<CancellationToken>()).Returns(_token);
        await FailAsync(Raw);

        errors.Count.ShouldBe(9);
        errors.Select(e => (e.Code, e.Message, e.Kind)).Distinct().ShouldHaveSingleItem().ShouldBe(("not-found", "Not found.", ResultErrorKind.NotFound));
    }

    [Fact]
    public async Task An_unknown_agent_author_shows_the_product_support_name()
    {
        _agents.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await Handler().HandleAsync(Raw, Ct);

        result.Value.Messages[1].AuthorDisplayName.ShouldBe("Orbitly Support");
    }
}

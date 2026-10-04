using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class GetTicketRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly FakeTimeProvider _clock = new();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly IRequesterRepository _requesters = Substitute.For<IRequesterRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ITagRepository _tags = Substitute.For<ITagRepository>();
    private readonly IKbRepository _kb = Substitute.For<IKbRepository>();
    private readonly Agent _sam;
    private readonly Requester _ann;
    private readonly Product _product;
    private readonly Ticket _ticket;

    public GetTicketRequestHandlerTests()
    {
        _sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _ann = Requester.Create("ann@example.com", "Ann", null, _clock).Value;
        _product = Product.Create("orbitly", "Orbitly", "ORB", null, _clock).Value;
        _ticket = TicketBuilder.New(_clock, _product.Id, _ann.Id, 42);
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        _tickets.GetByIdAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns(_ticket);
        _tickets.GetByNumberAsync("TS-42", Arg.Any<CancellationToken>()).Returns(_ticket);
        _tickets.GetMessagesAsync(_ticket.Id, false, Arg.Any<CancellationToken>()).Returns([]);
        _tickets.GetEventsAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns([]);
        _tickets.GetAttachmentsAsync(_ticket.Id, false, Arg.Any<CancellationToken>()).Returns([]);
        _kb.ListTicketArticlesAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns([]);
        _agents.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([_sam]);
        _requesters.GetByIdAsync(_ann.Id, Arg.Any<CancellationToken>()).Returns(_ann);
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(_product);
        _tags.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    private GetTicketRequestHandler Handler() => new(_claims, _agents, _tickets, _requesters, _products, _tags, _kb);

    private Message Msg(AuthorType type, Guid? authorId, MessageVisibility visibility, string body, int minute) =>
        Message.Restore(Guid.NewGuid(), _ticket.Id, type, authorId, visibility, body, null, null, _clock.GetUtcNow().AddMinutes(minute));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_ticket_is_found_by_id_or_by_number_case_insensitively(bool byId)
    {
        var result = await Handler().HandleAsync(byId ? _ticket.Id.ToString() : "ts-42", Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Id.ShouldBe(_ticket.Id),
            dto => dto.Number.ShouldBe("TS-42"),
            dto => dto.ProductName.ShouldBe("Orbitly"),
            dto => dto.Requester.Email.ShouldBe("ann@example.com"),
            dto => dto.RowVersion.ShouldBe(_ticket.Version),
            dto => dto.Channel.ShouldBe("Web"));
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    [InlineData("TS-99")]
    public async Task An_unknown_or_malformed_reference_is_not_found(string reference)
    {
        var result = await Handler().HandleAsync(reference, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("ticket-not-found");
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task An_unknown_id_is_not_found()
    {
        var result = await Handler().HandleAsync(Guid.NewGuid().ToString(), Ct);

        result.Errors[0].Code.ShouldBe("ticket-not-found");
    }

    [Fact]
    public async Task The_timeline_is_oldest_first_with_internal_notes_and_attachments_on_their_messages()
    {
        var first = Msg(AuthorType.Requester, _ann.Id, MessageVisibility.Public, "<p>one</p>", 0);
        var note = Msg(AuthorType.Agent, _sam.Id, MessageVisibility.Internal, "<p>note</p>", 1);
        _tickets.GetMessagesAsync(_ticket.Id, false, Arg.Any<CancellationToken>()).Returns([first, note]);
        _tickets.GetAttachmentsAsync(_ticket.Id, false, Arg.Any<CancellationToken>())
            .Returns([Attachment.Restore(Guid.NewGuid(), _ticket.Id, note.Id, "log.txt", "text/plain", 12, "k", _clock.GetUtcNow())]);
        var early = TicketEvent.Restore(Guid.NewGuid(), _ticket.Id, TicketEventType.Created, ActorType.Requester, _ann.Id, "{}", _clock.GetUtcNow());
        var late = TicketEvent.Restore(Guid.NewGuid(), _ticket.Id, TicketEventType.MessageAdded, ActorType.Agent, _sam.Id, "{\"messageId\":\"x\"}", _clock.GetUtcNow().AddMinutes(1));
        _tickets.GetEventsAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns([early, late]);

        var dto = (await Handler().HandleAsync("TS-42", Ct)).Value;

        dto.Messages.Select(m => m.Id).ShouldBe([first.Id, note.Id]);
        dto.Messages[1].ShouldSatisfyAllConditions(
            m => m.Visibility.ShouldBe("Internal"),
            m => m.BodyHtml.ShouldBe("<p>note</p>"),
            m => m.Attachments.ShouldHaveSingleItem().FileName.ShouldBe("log.txt"));
        dto.Messages[0].Attachments.ShouldBeEmpty();
        dto.Events.Select(e => e.Id).ShouldBe([early.Id, late.Id]);
        dto.Events[1].PayloadJson.ShouldBe("{\"messageId\":\"x\"}");
    }

    [Fact]
    public async Task Author_and_actor_names_resolve_for_agents_requesters_and_system()
    {
        _tickets.GetMessagesAsync(_ticket.Id, false, Arg.Any<CancellationToken>()).Returns([
            Msg(AuthorType.Requester, _ann.Id, MessageVisibility.Public, "a", 0),
            Msg(AuthorType.Agent, _sam.Id, MessageVisibility.Public, "b", 1),
            Msg(AuthorType.System, null, MessageVisibility.Public, "c", 2)]);
        _tickets.GetEventsAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns([
            TicketEvent.Restore(Guid.NewGuid(), _ticket.Id, TicketEventType.Created, ActorType.Requester, _ann.Id, "{}", _clock.GetUtcNow()),
            TicketEvent.Restore(Guid.NewGuid(), _ticket.Id, TicketEventType.Assigned, ActorType.Agent, _sam.Id, "{}", _clock.GetUtcNow()),
            TicketEvent.Restore(Guid.NewGuid(), _ticket.Id, TicketEventType.StatusChanged, ActorType.System, null, "{}", _clock.GetUtcNow())]);

        var dto = (await Handler().HandleAsync("TS-42", Ct)).Value;

        dto.Messages.Select(m => m.AuthorName).ShouldBe(["Ann", "Sam", null]);
        dto.Events.Select(e => e.ActorName).ShouldBe(["Ann", "Sam", "System"]);
    }

    [Fact]
    public async Task Linked_articles_appear_on_the_message_that_linked_them()
    {
        var first = Msg(AuthorType.Agent, _sam.Id, MessageVisibility.Public, "b", 0);
        var second = Msg(AuthorType.Agent, _sam.Id, MessageVisibility.Public, "c", 1);
        _tickets.GetMessagesAsync(_ticket.Id, false, Arg.Any<CancellationToken>()).Returns([first, second]);
        var article = KbArticle.Restore(Guid.NewGuid(), null, null, "reset-password", "Reset password", null, "x", KbArticleStatus.Draft, _sam.Id, _clock.GetUtcNow(), _clock.GetUtcNow(), null, 0);
        var missing = Guid.NewGuid();
        _kb.ListTicketArticlesAsync(_ticket.Id, Arg.Any<CancellationToken>())
            .Returns([new TicketArticle(_ticket.Id, first.Id, article.Id), new TicketArticle(_ticket.Id, second.Id, article.Id), new TicketArticle(_ticket.Id, first.Id, missing)]);
        _kb.GetArticleAsync(article.Id, Arg.Any<CancellationToken>()).Returns(article);

        var dto = (await Handler().HandleAsync("TS-42", Ct)).Value;

        dto.Messages[0].LinkedArticles.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            a => a.Slug.ShouldBe("reset-password"), a => a.Title.ShouldBe("Reset password"));
        dto.Messages[1].LinkedArticles.Count.ShouldBe(1);
        await _kb.Received(1).GetArticleAsync(article.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Names_are_loaded_in_batches_not_per_message()
    {
        _tickets.GetMessagesAsync(_ticket.Id, false, Arg.Any<CancellationToken>())
            .Returns([.. Enumerable.Range(0, 5).Select(i => Msg(AuthorType.Agent, _sam.Id, MessageVisibility.Public, "x", i))]);

        await Handler().HandleAsync("TS-42", Ct);

        await _agents.Received(1).GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await _agents.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_without_a_database_lookup()
    {
        _claims.Current.Returns((AgentClaims?)null);

        var result = await Handler().HandleAsync("TS-42", Ct);

        result.IsFailure.ShouldBeTrue();
        _tickets.ReceivedCalls().ShouldBeEmpty();
        _agents.ReceivedCalls().ShouldBeEmpty();
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Attachments;
using TechStrap.Application.Content;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class AddAgentReplyRequestHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketRepository _tickets;
    private List<(TicketEventType Type, string Payload)> _stagedEvents = [];
    private readonly IKbRepository _kb = Substitute.For<IKbRepository>();
    private readonly IAttachmentStore _store = Substitute.For<IAttachmentStore>();
    private readonly IMarkdownRenderer _markdown = Substitute.For<IMarkdownRenderer>();
    private readonly IHtmlSanitizer _sanitizer = Substitute.For<IHtmlSanitizer>();
    private readonly ITicketNotificationPlanner _planner = Substitute.For<ITicketNotificationPlanner>();
    private readonly Agent _sam;
    private readonly Ticket _ticket;
    private int _fileCounter;

    public AddAgentReplyRequestHandlerTests()
    {
        _tickets = TicketRepositorySubstitute.Create(ticket => _stagedEvents = [.. ticket.PendingEvents.Select(e => (e.Type, e.PayloadJson))]);
        _sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(_sam);
        _ticket = TicketBuilder.WithVersion(TicketBuilder.New(_clock), 7);
        _tickets.GetByIdAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns(_ticket);
        _tickets.GetStateAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns(_ => State(_ticket, 8));
        _markdown.ToHtml(Arg.Any<string>()).Returns(call => string.IsNullOrWhiteSpace(call.Arg<string>()) ? string.Empty : "<p>" + call.Arg<string>() + "</p>"); // like the real renderer, blank in gives nothing out
        _sanitizer.Sanitize(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _store.SaveAsync(Arg.Any<Guid>(), Arg.Any<IncomingAttachment>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var file = call.Arg<IncomingAttachment>();
            return Result<StoredAttachment>.Success(new StoredAttachment($"key-{++_fileCounter}", file.FileName, "image/png", file.Length));
        });
    }

    private static TicketState State(Ticket ticket, uint version) =>
        new(ticket.Id, ticket.Number.ToString(), ticket.Status, ticket.Priority, ticket.ProductId, ticket.AssigneeId, ticket.IsSpam, [], ticket.LastActivityAt, version);

    private AddAgentReplyRequestHandler Handler(IUnitOfWork? unitOfWork = null) =>
        new(_claims, _agents, _tickets, _kb, _store, _markdown, _sanitizer, _planner, unitOfWork ?? UnitOfWorkSubstitute.Create(), _clock,
            NullLogger<AddAgentReplyRequestHandler>.Instance);

    private Task<Result<AgentMessageResponse>> Reply(
        AddAgentReplyRequest request, IReadOnlyList<IncomingAttachment>? files = null, IUnitOfWork? unitOfWork = null, CancellationToken? ct = null) =>
        Handler(unitOfWork).HandleAsync(_ticket.Id, request, files ?? [], ct ?? Ct);

    private static AddAgentReplyRequest Request(string? body = "Hi **Ann**", Guid[]? articles = null, string? statusAfter = null, uint? rowVersion = null) =>
        new(body, articles, statusAfter, rowVersion);

    private static IncomingAttachment Png(string name = "shot.png", long length = 12) => new(name, "image/png", length, new MemoryStream([1, 2, 3]));

    private KbArticle GivenArticle(string slug)
    {
        var article = KbArticle.Create(null, null, slug, "Reset your password", null, "Steps.", _sam.Id, _clock).Value;
        _kb.GetArticleAsync(article.Id, Arg.Any<CancellationToken>()).Returns(article);
        return article;
    }

    [Fact]
    public async Task A_reply_renders_markdown_sanitises_saves_the_message_and_plans_the_email()
    {
        var result = await Reply(Request());

        result.IsSuccess.ShouldBeTrue();
        _markdown.Received(1).ToHtml("Hi **Ann**");
        _sanitizer.Received(1).Sanitize("<p>Hi **Ann**</p>");
        _tickets.Received(1).Update(_ticket);
        await _planner.Received(1).PlanAgentReplyAsync(
            _ticket, Arg.Is<Message>(m => m.Visibility == MessageVisibility.Public && m.Body == "<p>Hi **Ann**</p>"), _sam, false, Ct);
        result.Value.Message.ShouldSatisfyAllConditions(
            m => m.BodyHtml.ShouldBe("<p>Hi **Ann**</p>"),
            m => m.AuthorName.ShouldBe("Sam"),
            m => m.AuthorId.ShouldBe(_sam.Id),
            m => m.Visibility.ShouldBe("Public"));
        result.Value.Ticket.RowVersion.ShouldBe(8u);
    }

    [Fact]
    public async Task A_first_reply_on_an_open_ticket_moves_it_to_pending_and_sets_first_response_once()
    {
        var open = TicketBuilder.InStatus(TicketStatus.Open, _clock);
        _tickets.GetByIdAsync(open.Id, Arg.Any<CancellationToken>()).Returns(open);
        _tickets.GetStateAsync(open.Id, Arg.Any<CancellationToken>()).Returns(_ => State(open, 2));

        (await Handler().HandleAsync(open.Id, Request(), [], Ct)).IsSuccess.ShouldBeTrue();
        open.Status.ShouldBe(TicketStatus.Pending);
        var first = open.FirstResponseAt.ShouldNotBeNull();

        _clock.Advance(TimeSpan.FromHours(1));
        (await Handler().HandleAsync(open.Id, Request("Again"), [], Ct)).IsSuccess.ShouldBeTrue();
        open.Status.ShouldBe(TicketStatus.Pending);
        open.FirstResponseAt.ShouldBe(first);
    }

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.Open)]
    public async Task Send_and_solve_solves_after_the_reply_and_plans_one_email_marked_solved(TicketStatus start)
    {
        var ticket = TicketBuilder.InStatus(start, _clock);
        _tickets.GetByIdAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(ticket);
        _tickets.GetStateAsync(ticket.Id, Arg.Any<CancellationToken>()).Returns(_ => State(ticket, 8));

        var result = await Handler().HandleAsync(ticket.Id, Request(statusAfter: "Solved"), [], Ct);

        result.IsSuccess.ShouldBeTrue();
        ticket.Status.ShouldBe(TicketStatus.Solved);
        _stagedEvents.Select(e => e.Type).ShouldBe([TicketEventType.MessageAdded, TicketEventType.StatusChanged, TicketEventType.StatusChanged]);
        _stagedEvents[1].Payload.ShouldContain("Pending");
        _stagedEvents[2].Payload.ShouldContain("Solved");
        await _planner.Received(1).PlanAgentReplyAsync(ticket, Arg.Any<Message>(), _sam, true, Ct);
        await _planner.DidNotReceive().PlanSolvedAsync(Arg.Any<Ticket>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Closed")]
    public async Task A_reply_on_a_closed_ticket_is_409_ticket_closed_and_nothing_is_stored(string status)
    {
        var closed = TicketBuilder.InStatus(Enum.Parse<TicketStatus>(status), _clock);
        _tickets.GetByIdAsync(closed.Id, Arg.Any<CancellationToken>()).Returns(closed);

        var result = await Handler().HandleAsync(closed.Id, Request(), [Png()], Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict),
            e => e.Code.ShouldBe("ticket-closed"));
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, Ct);
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
        await _planner.DidNotReceiveWithAnyArgs().PlanAgentReplyAsync(default!, default!, default!, default, Ct);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task An_empty_body_is_400_body_required(string? body)
    {
        var result = await Reply(Request(body));

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation),
            e => e.Code.ShouldBe("body-required"));
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task An_over_long_body_is_rejected_before_markdown_runs()
    {
        var result = await Reply(Request(new string('a', DomainLimits.MessageBodyMaxLength + 1)));

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("body-too-long");
        _markdown.DidNotReceiveWithAnyArgs().ToHtml(default!);
        await _tickets.DidNotReceiveWithAnyArgs().GetByIdAsync(default, Ct);
    }

    [Fact]
    public async Task Too_many_files_or_too_many_bytes_are_rejected_before_anything_is_stored()
    {
        var many = Enumerable.Range(0, IntakeLimits.MaxFiles + 1).Select(i => Png($"f{i}.png")).ToList();
        var big = new[] { Png("a.png", IntakeLimits.MaxMessageBytes), Png("b.png", 1) };

        (await Reply(Request(), many)).Errors.ShouldHaveSingleItem().Code.ShouldBe("attachments-too-many");
        (await Reply(Request(), big)).Errors.ShouldHaveSingleItem().Code.ShouldBe("attachments-too-large");

        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, Ct);
        await _tickets.DidNotReceiveWithAnyArgs().GetByIdAsync(default, Ct);
    }

    [Fact]
    public async Task An_unknown_linked_article_is_400_and_nothing_is_stored()
    {
        var missing = Guid.NewGuid();

        var result = await Reply(Request(articles: [missing]), [Png()]);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation),
            e => e.Code.ShouldBe("article-not-found"),
            e => e.Target.ShouldBe("linkedArticleIds"));
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, Ct);
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task Too_many_linked_articles_is_a_field_error()
    {
        var ids = Enumerable.Range(0, TicketOperationLimits.MaxLinkedArticles + 1).Select(_ => Guid.NewGuid()).ToArray();

        var result = await Reply(Request(articles: ids));

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Code.ShouldBe("linked-articles-too-many"),
            e => e.Target.ShouldBe("linkedArticleIds"));
    }

    [Fact]
    public async Task Linked_articles_are_deduplicated_and_recorded_against_the_reply()
    {
        var article = GivenArticle("reset");

        var result = await Reply(Request(articles: [article.Id, article.Id]));

        result.IsSuccess.ShouldBeTrue();
        _kb.Received(1).AddTicketArticle(Arg.Is<TicketArticle>(link =>
            link.ArticleId == article.Id && link.TicketId == _ticket.Id && link.MessageId == result.Value.Message.Id));
        result.Value.Message.LinkedArticles.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            a => a.Id.ShouldBe(article.Id), a => a.Slug.ShouldBe("reset"), a => a.Title.ShouldBe("Reset your password"));
    }

    [Theory]
    [InlineData("solved")]
    [InlineData(" SOLVED ")]
    [InlineData("pending")]
    public async Task Status_after_is_parsed_case_insensitively(string statusAfter)
    {
        var result = await Reply(Request(statusAfter: statusAfter));

        result.IsSuccess.ShouldBeTrue();
        _ticket.Status.ShouldBe(statusAfter.Trim().Equals("solved", StringComparison.OrdinalIgnoreCase) ? TicketStatus.Solved : TicketStatus.Pending);
    }

    [Fact]
    public async Task A_failed_attachment_record_removes_files_already_stored()
    {
        _store.SaveAsync(Arg.Any<Guid>(), Arg.Is<IncomingAttachment>(f => f.FileName == "odd.png"), Arg.Any<CancellationToken>())
            .Returns(Result<StoredAttachment>.Success(new StoredAttachment("key-odd", "odd.png", "", 1)));

        var result = await Reply(Request(), [Png("ok.png"), Png("odd.png")]);

        result.IsFailure.ShouldBeTrue();
        await _store.Received(1).DeleteAsync("key-1", CancellationToken.None);
        await _store.Received(1).DeleteAsync("key-odd", CancellationToken.None);
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task A_failed_solve_removes_the_stored_files()
    {
        var solved = TicketBuilder.InStatus(TicketStatus.Solved, _clock);
        _tickets.GetByIdAsync(solved.Id, Arg.Any<CancellationToken>()).Returns(solved);

        var result = await Handler().HandleAsync(solved.Id, Request(statusAfter: "Solved"), [Png()], Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("invalid-status-transition");
        await _store.Received(1).DeleteAsync("key-1", CancellationToken.None);
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task A_state_read_that_throws_after_commit_keeps_the_committed_files()
    {
        _tickets.GetStateAsync(_ticket.Id, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db gone"));

        await Should.ThrowAsync<InvalidOperationException>(() => Reply(Request(), [Png()]));

        await _store.DidNotReceiveWithAnyArgs().DeleteAsync(default!, Ct);
    }

    [Fact]
    public async Task A_missing_state_after_commit_keeps_the_committed_files()
    {
        _tickets.GetStateAsync(_ticket.Id, Arg.Any<CancellationToken>()).Returns((TicketState?)null);

        var result = await Reply(Request(), [Png()]);

        result.IsFailure.ShouldBeTrue();
        await _store.DidNotReceiveWithAnyArgs().DeleteAsync(default!, Ct);
    }

    [Fact]
    public async Task The_state_read_after_commit_ignores_cancellation()
    {
        using var source = new CancellationTokenSource();

        await Reply(Request(), [Png()], ct: source.Token);

        await _tickets.Received().GetStateAsync(_ticket.Id, CancellationToken.None);
    }

    [Theory]
    [InlineData("Closed")]
    [InlineData("Done")]
    [InlineData("Open")]
    public async Task A_bad_status_after_is_a_field_error(string statusAfter)
    {
        var result = await Reply(Request(statusAfter: statusAfter));

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Validation),
            e => e.Code.ShouldBe("status-after-invalid"),
            e => e.Target.ShouldBe("statusAfter"));
        await _tickets.DidNotReceiveWithAnyArgs().GetByIdAsync(default, Ct);
    }

    [Fact]
    public async Task A_pending_status_after_means_no_extra_change()
    {
        (await Reply(Request(statusAfter: "Pending"))).IsSuccess.ShouldBeTrue();

        _ticket.Status.ShouldBe(TicketStatus.Pending);
    }

    [Fact]
    public async Task A_stale_optional_row_version_is_409_and_an_omitted_one_is_accepted()
    {
        var stale = await Reply(Request(rowVersion: 6));
        var omitted = await Reply(Request(rowVersion: null));

        stale.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        omitted.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_rejected_attachment_removes_files_already_stored()
    {
        var rejection = new ResultError("attachment-type-not-allowed", "Not allowed.", ResultErrorKind.Validation, "attachments");
        _store.SaveAsync(Arg.Any<Guid>(), Arg.Is<IncomingAttachment>(f => f.FileName == "bad.exe"), Arg.Any<CancellationToken>())
            .Returns(Result<StoredAttachment>.Failure(rejection));

        var result = await Reply(Request(), [Png("ok.png"), Png("bad.exe")]);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-type-not-allowed");
        await _store.Received(1).DeleteAsync("key-1", CancellationToken.None);
        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Fact]
    public async Task A_failed_commit_deletes_the_stored_attachments_and_returns_the_conflict()
    {
        var unitOfWork = UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ConcurrencyConflict));

        var result = await Reply(Request(), [Png("a.png"), Png("b.png")], unitOfWork);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        await _store.Received(1).DeleteAsync("key-1", CancellationToken.None);
        await _store.Received(1).DeleteAsync("key-2", CancellationToken.None);
    }

    [Fact]
    public async Task A_successful_commit_keeps_the_stored_attachments_and_reports_them()
    {
        var result = await Reply(Request(), [Png("a.png", 3)]);

        result.IsSuccess.ShouldBeTrue();
        await _store.DidNotReceiveWithAnyArgs().DeleteAsync(default!, Ct);
        result.Value.Message.Attachments.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            a => a.FileName.ShouldBe("a.png"), a => a.ContentType.ShouldBe("image/png"), a => a.Size.ShouldBe(3));
    }

    [Fact]
    public async Task An_exception_after_saving_deletes_the_stored_attachments_and_rethrows()
    {
        _planner.PlanAgentReplyAsync(default!, default!, default!, default, Ct).ThrowsAsyncForAnyArgs(new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() => Reply(Request(), [Png()]));

        await _store.Received(1).DeleteAsync("key-1", CancellationToken.None);
    }

    [Fact]
    public async Task The_planner_is_always_consulted_and_decides_who_is_emailed()
    {
        // The planner decides who is emailed (Task 7); the handler always asks it and never fails because it skips.
        // The erased-requester end-to-end case with the real planner is AgentReplyIntegrationTests.A_reply_to_an_erased_requester_is_saved_without_an_email.
        var result = await Reply(Request());

        result.IsSuccess.ShouldBeTrue();
        await _planner.Received(1).PlanAgentReplyAsync(_ticket, Arg.Any<Message>(), _sam, false, Ct);
        _tickets.Received(1).Update(_ticket);
    }

    [Fact]
    public async Task Cancellation_reaches_the_store_the_repositories_and_the_planner()
    {
        using var source = new CancellationTokenSource();
        var token = source.Token;
        var article = GivenArticle("reset");

        var result = await Reply(Request(articles: [article.Id]), [Png()], ct: token);

        result.IsSuccess.ShouldBeTrue();
        await _agents.Received().GetBySubjectAsync("sam", token);
        await _tickets.Received().GetByIdAsync(_ticket.Id, token);
        await _tickets.Received().GetStateAsync(_ticket.Id, CancellationToken.None);
        await _kb.Received().GetArticleAsync(article.Id, token);
        await _store.Received().SaveAsync(_ticket.Id, Arg.Any<IncomingAttachment>(), token);
        await _planner.Received().PlanAgentReplyAsync(_ticket, Arg.Any<Message>(), _sam, false, token);
    }
}

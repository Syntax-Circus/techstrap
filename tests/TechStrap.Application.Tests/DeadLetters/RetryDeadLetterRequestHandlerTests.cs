using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.DeadLetters;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Outbox;

namespace TechStrap.Application.Tests.DeadLetters;

public sealed class RetryDeadLetterRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly IEmailOutboxStore _store = Substitute.For<IEmailOutboxStore>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _admin;

    public RetryDeadLetterRequestHandlerTests()
    {
        _admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(_admin);
    }

    private RetryDeadLetterRequestHandler Handler(IUnitOfWork? unitOfWork = null) =>
        new(_store, _events, _agents, _claims, unitOfWork ?? UnitOfWorkSubstitute.Create(), _clock);

    private EmailOutboxItem Row(OutboxStatus status, int attempts = 5)
    {
        var item = EmailOutboxItem.Restore(
            Guid.CreateVersion7(), "ticket-confirmation", "ann@example.com", "{}", null, null, status, attempts,
            _clock.GetUtcNow(), null, null, "smtp-permanent", _clock.GetUtcNow(), null);
        _store.GetAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        return item;
    }

    [Fact]
    public async Task A_dead_letter_is_retried_and_audited_with_kind_and_the_attempts_it_had()
    {
        var item = Row(OutboxStatus.DeadLettered);

        var result = await Handler().HandleAsync(item.Id, Ct);

        result.IsSuccess.ShouldBeTrue();
        _store.Received(1).Update(item);
        item.Status.ShouldBe(OutboxStatus.Pending);
        item.Attempts.ShouldBe(0);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.DeadLetterRetried && e.SubjectType == AdminSubjectType.EmailOutbox && e.SubjectId == item.Id
            && e.PayloadJson == "{\"kind\":\"ticket-confirmation\",\"attempts\":5}"));
    }

    [Theory]
    [InlineData(OutboxStatus.Pending)]
    [InlineData(OutboxStatus.Sending)]
    [InlineData(OutboxStatus.Sent)]
    [InlineData(OutboxStatus.Discarded)]
    public async Task A_row_that_is_not_dead_lettered_is_a_409_and_nothing_is_staged(OutboxStatus status)
    {
        var item = Row(status, attempts: 2);

        var result = await Handler().HandleAsync(item.Id, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("outbox-not-dead-lettered"),
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict));
        _store.DidNotReceive().Update(Arg.Any<EmailOutboxItem>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task An_unknown_id_is_404_outbox_not_found()
    {
        var result = await Handler().HandleAsync(Guid.CreateVersion7(), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("outbox-not-found"),
            error => error.Kind.ShouldBe(ResultErrorKind.NotFound));
        _store.DidNotReceive().Update(Arg.Any<EmailOutboxItem>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task A_deactivated_actor_is_refused_before_the_row_is_read()
    {
        _admin.SetActive(false);

        var result = await Handler().HandleAsync(Guid.CreateVersion7(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(AgentErrors.Codes.Inactive);
        _ = _store.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _store.DidNotReceive().Update(Arg.Any<EmailOutboxItem>());
    }

    [Fact]
    public async Task A_commit_conflict_is_returned()
    {
        var item = Row(OutboxStatus.DeadLettered);

        var result = await Handler(UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("concurrency-conflict"))).HandleAsync(item.Id, Ct);

        result.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.Conflict);
    }
}

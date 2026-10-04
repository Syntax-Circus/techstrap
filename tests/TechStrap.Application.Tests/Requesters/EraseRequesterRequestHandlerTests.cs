using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Attachments;
using TechStrap.Application.Persistence;
using TechStrap.Application.Requesters;
using TechStrap.Application.Tests.Support;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Requesters;

namespace TechStrap.Application.Tests.Requesters;

public sealed class EraseRequesterRequestHandlerTests
{
    private readonly IRequesterRepository _requesters = Substitute.For<IRequesterRepository>();
    private readonly IRequesterErasure _erasure = Substitute.For<IRequesterErasure>();
    private readonly IAttachmentStore _attachments = Substitute.For<IAttachmentStore>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _admin;
    private readonly Requester _requester;

    public EraseRequesterRequestHandlerTests()
    {
        _admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(_admin);
        _requester = Requester.Create("ann@example.com", "Ann", null, _clock).Value;
        _requesters.GetByIdAsync(_requester.Id, Arg.Any<CancellationToken>()).Returns(_requester);
        _erasure.EraseDataAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new RequesterErasureResult(2, 5, 1, 3, 4, ["k1", "k2"]));
    }

    private EraseRequesterRequestHandler Handler(IUnitOfWork? unitOfWork = null) =>
        new(_requesters, _erasure, _attachments, _events, _agents, _claims, unitOfWork ?? UnitOfWorkSubstitute.Create(), _clock,
            NullLogger<EraseRequesterRequestHandler>.Instance);

    [Fact]
    public async Task The_old_email_reaches_the_erasure_port_before_the_requester_is_anonymised()
    {
        var result = await Handler().HandleAsync(_requester.Id, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _erasure.Received(1).EraseDataAsync(_requester.Id, "ann@example.com", _clock.GetUtcNow(), Arg.Any<CancellationToken>());
        _requester.IsErased.ShouldBeTrue();
        _requesters.Received(1).Update(_requester);
    }

    [Fact]
    public async Task The_erasure_is_audited_with_counts_only()
    {
        await Handler().HandleAsync(_requester.Id, TestContext.Current.CancellationToken);

        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.RequesterErased && e.SubjectType == AdminSubjectType.Requester && e.SubjectId == _requester.Id
            && e.PayloadJson == "{\"tickets\":2,\"messages\":5,\"attachments\":1,\"links\":3,\"outboxRows\":4}"));
    }

    [Fact]
    public async Task Files_are_deleted_only_after_a_successful_commit()
    {
        var conflict = await Handler(UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("concurrency-conflict")))
            .HandleAsync(_requester.Id, TestContext.Current.CancellationToken);

        conflict.Errors.ShouldHaveSingleItem().Code.ShouldBe("concurrency-conflict");
        await _attachments.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        (await Handler().HandleAsync(_requester.Id, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        await _attachments.Received(1).DeleteAsync("k1", CancellationToken.None);
        await _attachments.Received(1).DeleteAsync("k2", CancellationToken.None);
    }

    [Fact]
    public async Task An_unknown_requester_is_not_found_and_nothing_is_staged()
    {
        var result = await Handler().HandleAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("requester-not-found"),
            error => error.Kind.ShouldBe(ResultErrorKind.NotFound));
        await _erasure.DidNotReceive().EraseDataAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        _requesters.DidNotReceive().Update(Arg.Any<Requester>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task Erasing_an_erased_requester_runs_again_and_succeeds()
    {
        _requester.Erase(_clock);

        var result = await Handler().HandleAsync(_requester.Id, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _erasure.Received(1).EraseDataAsync(_requester.Id, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        _events.Received(1).Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task A_deactivated_actor_is_refused_before_anything_is_read()
    {
        _admin.SetActive(false);

        var result = await Handler().HandleAsync(_requester.Id, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(AgentErrors.Codes.Inactive);
        _ = _requesters.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _erasure.DidNotReceive().EraseDataAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        _requesters.DidNotReceive().Update(Arg.Any<Requester>());
    }

    [Fact]
    public async Task A_commit_conflict_is_returned_and_no_file_is_deleted()
    {
        var result = await Handler(UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("concurrency-conflict")))
            .HandleAsync(_requester.Id, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.Conflict);
        await _attachments.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

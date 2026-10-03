using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.AdminEvents;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.AdminEvents;

public sealed class ListAdminEventsRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly FakeTimeProvider _clock = new();

    private ListAdminEventsRequestHandler Handler() => new(_events, _agents);

    private AdminEvent EventBy(Guid actorId, AdminEventType type = AdminEventType.TagCreated, AdminSubjectType subject = AdminSubjectType.Tag) =>
        AdminEvent.Record(type, actorId, subject, Guid.CreateVersion7(), "{\"slug\":\"bug\"}", _clock).Value;

    [Fact]
    public async Task Events_map_to_dtos_with_the_actor_name_as_label()
    {
        var named = Agent.Create("s1", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        var unnamed = Agent.Create("s2", null, "riley@example.com", AgentRole.Admin, _clock).Value;
        var first = EventBy(named.Id);
        var second = EventBy(unnamed.Id, AdminEventType.ProductCreated, AdminSubjectType.Product);
        _events.ListAsync(Arg.Any<AdminEventFilter>(), 1, 25, Ct).Returns(new PagedResult<AdminEvent>([first, second], 1, 25, 2));
        _agents.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Ct).Returns([named, unnamed]);

        var page = (await Handler().HandleAsync(null, null, null, 1, 25, Ct)).Value;

        page.TotalCount.ShouldBe(2);
        page.Items[0].ShouldSatisfyAllConditions(
            item => item.ActorLabel.ShouldBe("Sam"),
            item => item.Type.ShouldBe("TagCreated"),
            item => item.SubjectType.ShouldBe("Tag"),
            item => item.Payload.ShouldBe("{\"slug\":\"bug\"}"));
        page.Items[1].ActorLabel.ShouldBe("riley@example.com");
        page.Items[1].SubjectType.ShouldBe("Product");
    }

    [Fact]
    public async Task An_event_whose_actor_no_longer_exists_has_no_label()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), 1, 25, Ct).Returns(new PagedResult<AdminEvent>([EventBy(Guid.CreateVersion7())], 1, 25, 1));
        _agents.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Ct).Returns([]);

        var page = (await Handler().HandleAsync(null, null, null, 1, 25, Ct)).Value;

        page.Items.ShouldHaveSingleItem().ActorLabel.ShouldBeNull();
    }

    [Fact]
    public async Task A_known_subject_type_filter_is_passed_to_the_repository()
    {
        var actor = Guid.CreateVersion7();
        var asOf = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
        _events.ListAsync(Arg.Any<AdminEventFilter>(), 2, 10, Ct).Returns(new PagedResult<AdminEvent>([], 2, 10, 0));
        _agents.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Ct).Returns([]);

        (await Handler().HandleAsync("tag", actor, asOf, 2, 10, Ct)).IsSuccess.ShouldBeTrue();

        await _events.Received(1).ListAsync(new AdminEventFilter(AdminSubjectType.Tag, actor, asOf), 2, 10, Ct);
    }

    [Theory]
    [InlineData("widget")]
    [InlineData("99")]
    public async Task An_unknown_subject_type_is_a_field_error(string value)
    {
        var result = await Handler().HandleAsync(value, null, null, 1, 25, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("admin-event-subject-type-invalid"),
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("subjectType"));
        await _events.DidNotReceiveWithAnyArgs().ListAsync(default!, default, default, Ct);
    }
}

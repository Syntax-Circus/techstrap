using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Auditing;

public sealed class AdminAuditTests
{
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _actor = Agent.Create("sub-1", "Sam", "sam@example.com", AgentRole.Admin, new FakeTimeProvider()).Value;

    [Fact]
    public void The_event_is_staged_with_a_camel_case_payload_and_the_acting_agent()
    {
        var subjectId = Guid.CreateVersion7();

        AdminAudit.Record(_events, AdminEventType.TagCreated, _actor, AdminSubjectType.Tag, subjectId, new { Slug = "bug" }, _clock);

        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.TagCreated && e.ActorId == _actor.Id && e.SubjectId == subjectId && e.PayloadJson == "{\"slug\":\"bug\"}"));
    }

    [Fact]
    public void A_payload_with_personal_data_is_a_programming_error() =>
        Should.Throw<InvalidOperationException>(() =>
            AdminAudit.Record(_events, AdminEventType.TagCreated, _actor, AdminSubjectType.Tag, Guid.CreateVersion7(), new { Email = "x@example.com" }, _clock));
}

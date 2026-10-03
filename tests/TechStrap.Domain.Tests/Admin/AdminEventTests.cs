using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Admin;

namespace TechStrap.Domain.Tests.Admin;

public sealed class AdminEventTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void An_event_records_actor_subject_and_time()
    {
        var actor = Guid.NewGuid();
        var subject = Guid.NewGuid();

        var admin = AdminEvent.Record(AdminEventType.RequesterErased, actor, AdminSubjectType.Requester, subject, null, _clock).Value;

        admin.ActorId.ShouldBe(actor);
        admin.SubjectId.ShouldBe(subject);
        admin.OccurredAt.ShouldBe(_clock.GetUtcNow());
        admin.PayloadJson.ShouldBe("{}");
    }

    [Fact]
    public void A_payload_of_ids_and_enum_names_is_accepted()
    {
        AdminEvent.Record(AdminEventType.ApiKeyCreated, Guid.NewGuid(), AdminSubjectType.ApiKey, Guid.NewGuid(), "{\"productId\":\"1\",\"kind\":\"Public\"}", _clock)
            .IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("{\"email\":\"ann@example.com\"}")]
    [InlineData("{\"Name\":\"Ann\"}")]
    [InlineData("{\"nested\":{\"token\":\"abc\"}}")]
    [InlineData("{\"items\":[{\"secret\":\"x\"}]}")]
    [InlineData("{\"key\":\"tsk_live\"}")]
    [InlineData("[1]")]
    [InlineData("not json")]
    public void A_payload_with_personal_data_secrets_or_a_wrong_shape_is_rejected(string payload)
    {
        AdminEvent.Record(AdminEventType.ProductUpdated, Guid.NewGuid(), AdminSubjectType.Product, Guid.NewGuid(), payload, _clock)
            .Error!.Code.ShouldBe("admin-event-payload-invalid");
    }
}

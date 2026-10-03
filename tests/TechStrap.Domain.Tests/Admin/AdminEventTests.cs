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
    [InlineData("{\"Name\":\"x\"}")]
    [InlineData("{\"key\":\"x\"}")]
    [InlineData("{\"body\":\"x\"}")]
    [InlineData("{\"subject\":\"x\"}")]
    [InlineData("{\"address\":\"x\"}")]
    [InlineData("{\"apiKeyValue\":\"x\"}")]
    [InlineData("{\"rawToken\":\"x\"}")]
    [InlineData("{\"accessToken\":\"x\"}")]
    [InlineData("{\"tokenHash\":\"x\"}")]
    [InlineData("{\"keyHash\":\"x\"}")]
    [InlineData("{\"Authorization\":\"x\"}")]
    [InlineData("{\"requesterEmail\":\"x\"}")]
    [InlineData("{\"items\":[{\"a\":{\"clientSecret\":\"x\"}}]}")]
    public void A_property_name_containing_a_sensitive_word_is_rejected_at_any_depth(string payload)
    {
        AdminEvent.Record(AdminEventType.ProductUpdated, Guid.NewGuid(), AdminSubjectType.Product, Guid.NewGuid(), payload, _clock)
            .Error!.Code.ShouldBe("admin-event-payload-invalid");
    }

    [Theory]
    [InlineData("{\"productId\":\"1\",\"kind\":\"Public\",\"keyPrefix\":\"tsk_ab\"}")]
    [InlineData("{\"productKey\":\"1\"}")]
    [InlineData("{\"displayName\":\"x\"}")]
    [InlineData("{\"keyPrefix\":\"tsk_ab\"}")]
    public void Id_kind_and_prefix_fields_are_accepted(string payload)
    {
        AdminEvent.Record(AdminEventType.ApiKeyCreated, Guid.NewGuid(), AdminSubjectType.ApiKey, Guid.NewGuid(), payload, _clock)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_actor_or_subject_id_is_rejected()
    {
        AdminEvent.Record(AdminEventType.ProductUpdated, Guid.Empty, AdminSubjectType.Product, Guid.NewGuid(), null, _clock).Error!.Code.ShouldBe("actor-id-required");
        AdminEvent.Record(AdminEventType.ProductUpdated, Guid.NewGuid(), AdminSubjectType.Product, Guid.Empty, null, _clock).Error!.Code.ShouldBe("subject-id-required");
    }

    [Fact]
    public void The_occurrence_time_is_whole_microseconds()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero).AddTicks(3));

        var admin = AdminEvent.Record(AdminEventType.ProductUpdated, Guid.NewGuid(), AdminSubjectType.Product, Guid.NewGuid(), null, clock).Value;

        (admin.OccurredAt.Ticks % 10).ShouldBe(0);
    }

    [Theory]
    [InlineData("{\"email\":\"ann@example.com\"}")]
    [InlineData("{\"nested\":{\"token\":\"abc\"}}")]
    [InlineData("{\"items\":[{\"secret\":\"x\"}]}")]
    [InlineData("[1]")]
    [InlineData("not json")]
    public void A_payload_with_personal_data_secrets_or_a_wrong_shape_is_rejected(string payload)
    {
        AdminEvent.Record(AdminEventType.ProductUpdated, Guid.NewGuid(), AdminSubjectType.Product, Guid.NewGuid(), payload, _clock)
            .Error!.Code.ShouldBe("admin-event-payload-invalid");
    }
}

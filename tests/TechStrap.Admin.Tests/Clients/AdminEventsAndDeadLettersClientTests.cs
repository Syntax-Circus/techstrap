using System.Net;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.DeadLetters;
using TechStrap.Contracts.Paging;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

public sealed class AdminEventsAndDeadLettersClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid ActorId = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid LetterId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    private static AdminEventDto Event(string type = AdminEventTypes.TagCreated, string subject = AdminSubjectTypes.Tag) =>
        new(Guid.NewGuid(), type, ActorId, "Ada", subject, Guid.NewGuid(), "{\"slug\":\"bug\"}", new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));

    private static DeadLetterDto Letter() =>
        new(LetterId, "agent-reply", "a***@example.com", Guid.NewGuid(), Guid.NewGuid(), 5, "smtp-transient", new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Events_send_the_filter_and_the_paging_as_the_query_string()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/admin-events", new PagedResponse<AdminEventDto>([Event()], 2, 25, 51));
        var asOf = new DateTimeOffset(2026, 10, 4, 12, 30, 15, TimeSpan.Zero).AddTicks(1234567);

        var result = await api.Get<IAdminEventsClient>().ListAsync(new AdminEventFilter(AdminSubjectTypes.Tag, ActorId, asOf), 2, 25, Ct);

        result.Value.TotalCount.ShouldBe(51);
        result.Value.Items.Single().Type.ShouldBe("TagCreated");
        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe(
            $"?subjectType=Tag&actorId={ActorId}&asOf=2026-10-04T12%3A30%3A15.1234567%2B00%3A00&page=2&pageSize=25");
    }

    [Fact]
    public async Task An_empty_filter_sends_only_the_paging()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/admin-events", new PagedResponse<AdminEventDto>([], 1, 25, 0));

        await api.Get<IAdminEventsClient>().ListAsync(new AdminEventFilter(), 1, 25, Ct);

        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe("?page=1&pageSize=25");
    }

    [Fact]
    public async Task An_unknown_subject_type_is_a_validation_error_on_the_subject_type_field()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnValidationProblem(HttpMethod.Get, "/api/admin-events", "subjectType", ApiErrorCodes.AdminEventSubjectTypeInvalid, "Use one of: Product, ApiKey.");

        var result = await api.Get<IAdminEventsClient>().ListAsync(new AdminEventFilter("Nonsense"), 1, 25, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.AdminEventSubjectTypeInvalid, "Use one of: Product, ApiKey.", ResultErrorKind.Validation, "subjectType"));
    }

    [Fact]
    public async Task Dead_letters_are_listed_with_their_paging()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/dead-letters", new PagedResponse<DeadLetterDto>([Letter()], 1, 25, 1));

        var result = await api.Get<IDeadLettersClient>().ListAsync(1, 25, Ct);

        result.Value.Items.Single().ShouldSatisfyAllConditions(
            l => l.Recipient.ShouldBe("a***@example.com"),
            l => l.LastError.ShouldBe("smtp-transient"),
            l => l.Attempts.ShouldBe(5));
        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe("?page=1&pageSize=25");
    }

    [Fact]
    public async Task Count_is_the_total_of_a_page_of_one()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/dead-letters", new PagedResponse<DeadLetterDto>([Letter()], 1, 1, 7));

        var result = await api.Get<IDeadLettersClient>().CountAsync(Ct);

        result.Value.ShouldBe(7);
        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe("?page=1&pageSize=1");
    }

    [Fact]
    public async Task A_failed_count_is_a_failure_with_the_api_code_not_a_zero()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnProblem(HttpMethod.Get, "/api/dead-letters", HttpStatusCode.Forbidden, ApiErrorCodes.AdminAccessRequired, "Only admins may do this.");

        var result = await api.Get<IDeadLettersClient>().CountAsync(Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].ShouldBe(new ResultError(ApiErrorCodes.AdminAccessRequired, "Only admins may do this.", ResultErrorKind.Forbidden));
    }

    [Fact]
    public async Task Retry_posts_and_discard_deletes_the_letter_and_neither_is_ever_retried()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnStatus(HttpMethod.Post, $"/api/dead-letters/{LetterId}/retry", HttpStatusCode.ServiceUnavailable);
        api.Stub.OnStatus(HttpMethod.Delete, $"/api/dead-letters/{LetterId}", HttpStatusCode.ServiceUnavailable);
        var client = api.Get<IDeadLettersClient>();

        var retry = await client.RetryAsync(LetterId, Ct);
        var discard = await client.DiscardAsync(LetterId, Ct);

        ApiErrorCodes.IsUncertainWrite(retry.Errors[0].Code).ShouldBeTrue();
        ApiErrorCodes.IsUncertainWrite(discard.Errors[0].Code).ShouldBeTrue();
        api.Stub.Count(HttpMethod.Post, $"/api/dead-letters/{LetterId}/retry").ShouldBe(1);
        api.Stub.Count(HttpMethod.Delete, $"/api/dead-letters/{LetterId}").ShouldBe(1);
    }

    [Fact]
    public async Task Retry_and_discard_succeed_on_204_and_keep_the_not_found_and_not_dead_lettered_codes()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnStatus(HttpMethod.Post, $"/api/dead-letters/{LetterId}/retry", HttpStatusCode.NoContent);
        api.Stub.OnProblem(HttpMethod.Delete, $"/api/dead-letters/{LetterId}", HttpStatusCode.Conflict, ApiErrorCodes.OutboxNotDeadLettered, "Only a dead-lettered email can be discarded.");
        var client = api.Get<IDeadLettersClient>();

        (await client.RetryAsync(LetterId, Ct)).IsSuccess.ShouldBeTrue();
        (await client.DiscardAsync(LetterId, Ct)).Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.OutboxNotDeadLettered);

        api.Stub.OnProblem(HttpMethod.Post, $"/api/dead-letters/{LetterId}/retry", HttpStatusCode.NotFound, ApiErrorCodes.OutboxNotFound, "No such email.");
        (await client.RetryAsync(LetterId, Ct)).Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.OutboxNotFound);
    }

    [Fact]
    public async Task Both_new_clients_are_registered_and_send_the_admins_token()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/dead-letters", new PagedResponse<DeadLetterDto>([], 1, 1, 0));
        api.Stub.OnJson(HttpMethod.Get, "/api/admin-events", new PagedResponse<AdminEventDto>([], 1, 25, 0));

        await api.Get<IDeadLettersClient>().CountAsync(Ct);
        await api.Get<IAdminEventsClient>().ListAsync(new AdminEventFilter(), 1, 25, Ct);

        api.Stub.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }
}

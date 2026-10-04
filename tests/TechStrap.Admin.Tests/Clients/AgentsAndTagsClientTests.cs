using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tags;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

public sealed class AgentsAndTagsClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid AgentId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid TagId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static JsonElement Body(ApiHarness api) => JsonDocument.Parse(api.Stub.Requests.Last().Body!).RootElement;

    [Fact]
    public async Task ListPage_sends_the_page_and_the_page_size_and_returns_the_admin_fields()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        var row = new AgentListItemDto(AgentId, "Sam", "Sam (Orbitly)", "sam@example.com", AgentRoles.Agent, true, DateTimeOffset.UtcNow);
        api.Stub.OnJson(HttpMethod.Get, "/api/agents", new PagedResponse<AgentListItemDto>([row], 3, 25, 61));

        var result = await api.Get<IAgentsClient>().ListPageAsync(3, 25, Ct);

        result.Value.TotalCount.ShouldBe(61);
        result.Value.Items.Single().ShouldSatisfyAllConditions(
            r => r.Email.ShouldBe("sam@example.com"),
            r => r.Role.ShouldBe(AgentRoles.Agent),
            r => r.IsActive.ShouldBe(true));
        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe("?page=3&pageSize=25");
    }

    [Fact]
    public async Task SetActive_puts_only_the_flag_and_returns_the_agent()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Put, $"/api/agents/{AgentId}", new AgentDto(AgentId, "Sam", "sam@example.com", AgentRoles.Agent, false, null, null));

        var result = await api.Get<IAgentsClient>().SetActiveAsync(AgentId, false, Ct);

        result.Value.IsActive.ShouldBeFalse();
        var body = Body(api);
        body.EnumerateObject().Select(p => p.Name).ShouldBe(["isActive"]);
        body.GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Deactivating_the_last_active_admin_is_a_conflict_with_its_own_code_and_message()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnProblem(HttpMethod.Put, $"/api/agents/{AgentId}", HttpStatusCode.Conflict, ApiErrorCodes.LastActiveAdmin, "TechStrap needs at least one active admin.");

        var result = await api.Get<IAgentsClient>().SetActiveAsync(AgentId, false, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.LastActiveAdmin, "TechStrap needs at least one active admin.", ResultErrorKind.Conflict));
    }

    [Fact]
    public async Task UpdateMyProfile_puts_the_name_and_a_204_is_a_success()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnStatus(HttpMethod.Put, "/api/agents/me/profile", HttpStatusCode.NoContent);

        var result = await api.Get<IAgentsClient>().UpdateMyProfileAsync(new UpdateMyProfileRequest("Samantha"), Ct);

        result.IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("publicDisplayName").GetString().ShouldBe("Samantha");
    }

    [Theory]
    [InlineData("public-display-name-too-long")]
    [InlineData("public-display-name-invalid")]
    public async Task UpdateMyProfile_maps_a_rejected_name_to_the_public_display_name_field(string code)
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnValidationProblem(HttpMethod.Put, "/api/agents/me/profile", ApiFields.PublicDisplayName, code, "A public display name is plain text without '@'.");

        var result = await api.Get<IAgentsClient>().UpdateMyProfileAsync(new UpdateMyProfileRequest("sam@x"), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(code, "A public display name is plain text without '@'.", ResultErrorKind.Validation, "public-display-name"));
    }

    [Fact]
    public async Task Notification_preferences_are_read_as_a_list_and_written_as_the_full_set()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub
            .OnJson(HttpMethod.Get, "/api/agents/me/notification-preferences", new[] { new NotificationPreferenceDto(ProductId, "Orbitly", true), new NotificationPreferenceDto(Guid.NewGuid(), "Acme", false) })
            .OnStatus(HttpMethod.Put, "/api/agents/me/notification-preferences", HttpStatusCode.NoContent);
        var client = api.Get<IAgentsClient>();

        var read = await client.GetNotificationPreferencesAsync(Ct);
        var write = await client.UpdateNotificationPreferencesAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(ProductId, false), new NotificationPreferenceUpdateDto(Guid.Parse("dddddddd-0000-0000-0000-000000000001"), true)]), Ct);

        read.Value.Select(p => (p.ProductName, p.NotifyNewTicket)).ShouldBe([("Orbitly", true), ("Acme", false)]);
        write.IsSuccess.ShouldBeTrue();
        var preferences = Body(api).GetProperty("preferences").EnumerateArray().ToList();
        preferences.Count.ShouldBe(2);
        preferences[0].GetProperty("productId").GetGuid().ShouldBe(ProductId);
        preferences[0].GetProperty("notifyNewTicket").GetBoolean().ShouldBeFalse();
        preferences[1].GetProperty("notifyNewTicket").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_summary_is_read_from_the_summary_route_and_keeps_the_ticket_count()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/tags/summary", new[] { new TagSummaryDto(TagId, "bug", "Bug", "#DC2626", 12) });

        var result = await api.Get<ITagsClient>().ListSummaryAsync(Ct);

        result.Value.Single().ShouldBe(new TagSummaryDto(TagId, "bug", "Bug", "#DC2626", 12));
        api.Stub.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/tags/summary");
    }

    [Fact]
    public async Task Create_and_update_send_the_request_and_return_the_tag()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        var tag = new TagDto(TagId, "bug", "Bug", "#DC2626");
        api.Stub.OnJson(HttpMethod.Post, "/api/tags", tag, HttpStatusCode.Created).OnJson(HttpMethod.Put, $"/api/tags/{TagId}", tag);
        var client = api.Get<ITagsClient>();

        (await client.CreateAsync(new CreateTagRequest("bug", "Bug", "#dc2626"), Ct)).Value.ShouldBe(tag);
        var created = JsonDocument.Parse(api.Stub.Requests[0].Body!).RootElement;
        created.GetProperty("slug").GetString().ShouldBe("bug");
        created.GetProperty("colour").GetString().ShouldBe("#dc2626");

        (await client.UpdateAsync(TagId, new UpdateTagRequest("Defect", "#2563EB"), Ct)).IsSuccess.ShouldBeTrue();
        api.Stub.Requests[1].Method.ShouldBe(HttpMethod.Put);
        JsonDocument.Parse(api.Stub.Requests[1].Body!).RootElement.GetProperty("name").GetString().ShouldBe("Defect");
    }

    [Fact]
    public async Task A_duplicate_slug_is_a_conflict_with_the_tag_slug_taken_code()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnProblem(HttpMethod.Post, "/api/tags", HttpStatusCode.Conflict, ApiErrorCodes.TagSlugTaken, "Another tag already uses this slug.");

        var result = await api.Get<ITagsClient>().CreateAsync(new CreateTagRequest("bug", "Bug", "#DC2626"), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.TagSlugTaken, "Another tag already uses this slug.", ResultErrorKind.Conflict));
    }

    [Fact]
    public async Task Delete_without_force_sends_no_query_and_with_force_sends_force_true()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnStatus(HttpMethod.Delete, $"/api/tags/{TagId}", HttpStatusCode.NoContent);
        var client = api.Get<ITagsClient>();

        (await client.DeleteAsync(TagId, force: false, Ct)).IsSuccess.ShouldBeTrue();
        (await client.DeleteAsync(TagId, force: true, Ct)).IsSuccess.ShouldBeTrue();

        api.Stub.Requests.Select(r => r.Query).ShouldBe(["", "?force=true"]);
        api.Stub.Requests.ShouldAllBe(r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task A_tag_in_use_is_a_conflict_whose_message_keeps_the_count()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnProblem(HttpMethod.Delete, $"/api/tags/{TagId}", HttpStatusCode.Conflict, ApiErrorCodes.TagInUse, "This tag is on 3 tickets. Delete it with force to remove it from them first.");

        var result = await api.Get<ITagsClient>().DeleteAsync(TagId, force: false, Ct);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(ApiErrorCodes.TagInUse);
        error.Kind.ShouldBe(ResultErrorKind.Conflict);
        error.Message.ShouldContain("3 tickets");
    }
}

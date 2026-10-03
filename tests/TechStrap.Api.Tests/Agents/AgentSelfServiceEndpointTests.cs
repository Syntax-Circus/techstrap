using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Agents;

namespace TechStrap.Api.Tests.Agents;

public sealed class AgentSelfServiceEndpointTests(TestPostgres postgres)
{
    [Fact]
    public async Task A_display_name_persists_and_is_returned_by_me()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var client = factory.CreateClient().Bearer(TestJwt.Token("me", [TestJwt.AgentGroup], email: "riley@example.com", name: "Riley Chen"));
        (await client.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        using var saved = await client.PutAsJsonAsync("/api/agents/me/profile", new UpdateMyProfileRequest("Ry"), TestContext.Current.CancellationToken);
        var me = await client.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken);

        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        me!.PublicDisplayName.ShouldBe("Ry");
    }

    [Fact]
    public async Task An_invalid_display_name_is_a_400_with_a_field_error()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var client = factory.CreateClient().Bearer(TestJwt.Token("me", [TestJwt.AgentGroup], email: "riley@example.com"));
        (await client.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        using var response = await client.PutAsJsonAsync("/api/agents/me/profile", new UpdateMyProfileRequest("ry@example.com"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("public-display-name");
    }

    [Fact]
    public async Task Sending_the_same_preference_update_twice_succeeds_and_keeps_one_row()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        using var client = factory.CreateClient().Bearer(TestJwt.Token("me", [TestJwt.AgentGroup], email: "riley@example.com"));
        (await admin.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var me = (await client.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken))!;
        using var created = await admin.PostAsJsonAsync("/api/products", new TechStrap.Contracts.Products.CreateProductRequest("orbitly", "Orbitly", "ORB", null), TestContext.Current.CancellationToken);
        var product = (await created.Content.ReadFromJsonAsync<TechStrap.Contracts.Products.ProductDto>(TestContext.Current.CancellationToken))!;
        var update = new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(product.Id, true)]);

        using var first = await client.PutAsJsonAsync("/api/agents/me/notification-preferences", update, TestContext.Current.CancellationToken);
        using var second = await client.PutAsJsonAsync("/api/agents/me/notification-preferences", update, TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await database.ScalarAsync<long>(
            $"SELECT count(*) FROM agent_notification_preferences WHERE agent_id = '{me.Id}' AND product_id = '{product.Id}' AND notify_new_ticket")).ShouldBe(1);
        (await database.ScalarAsync<long>($"SELECT count(*) FROM agent_notification_preferences WHERE agent_id = '{me.Id}'")).ShouldBe(1);
    }
}

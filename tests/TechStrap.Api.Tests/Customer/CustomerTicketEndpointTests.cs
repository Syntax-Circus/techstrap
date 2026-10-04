using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Customer;

public sealed class CustomerTicketEndpointTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, CustomerSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: new Dictionary<string, string?>(database.Settings));
        return (factory, await CustomerTestData.SeedAsync(factory, Ct));
    }

    private static HttpRequestMessage Get(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket");
        if (token is not null)
        {
            request.Headers.Add(HeaderNames.TicketToken, token);
        }

        return request;
    }

    [Fact]
    public async Task The_customer_sees_the_ticket_with_public_messages_and_no_store()
    {
        var (factory, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Get(seed.ValidToken), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var raw = await response.Content.ReadAsStringAsync(Ct);
        raw.ShouldNotContain(CustomerTestData.InternalNoteText);
        raw.ShouldNotContain(seed.InternalAttachmentId.ToString());
        raw.ShouldNotContain("sam@example.com");
        raw.ShouldNotContain("Hargreaves");
        var dto = System.Text.Json.JsonSerializer.Deserialize<CustomerTicketDto>(raw, System.Text.Json.JsonSerializerOptions.Web)!;
        dto.Number.ShouldBe(seed.Number);
        dto.Subject.ShouldBe("Login broken");
        dto.Messages.Count.ShouldBe(2);
        var agentMessage = dto.Messages.Single(m => m.AuthorType == "Agent");
        agentMessage.AuthorDisplayName.ShouldBe("Sam from Orbitly Support");
        agentMessage.Attachments.ShouldHaveSingleItem().Id.ShouldBe(seed.PublicAttachmentId);
        dto.Messages.Single(m => m.AuthorType == "Requester").AuthorDisplayName.ShouldBeNull();
    }

    [Fact]
    public async Task A_bearer_or_api_key_on_the_request_changes_nothing()
    {
        var (factory, seed) = await StartAsync();
        await using var _f = factory;
        using var client = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));

        using var withToken = Get(seed.ValidToken);
        withToken.Headers.Add(HeaderNames.ApiKey, "tsk_not-a-real-key");
        using var ok = await client.SendAsync(withToken, Ct);
        using var noToken = Get(null);
        noToken.Headers.Add(HeaderNames.ApiKey, "tsk_not-a-real-key");
        using var missing = await client.SendAsync(noToken, Ct);

        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

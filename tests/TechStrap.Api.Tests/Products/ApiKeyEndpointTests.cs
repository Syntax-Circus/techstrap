using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Products;

namespace TechStrap.Api.Tests.Products;

public sealed class ApiKeyEndpointTests(TestPostgres postgres)
{
    private static async Task SignInAsync(HttpClient client) =>
        (await client.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    private static async Task<ProductDto> CreateProductAsync(HttpClient admin, string key, string prefix)
    {
        using var response = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest(key, key, prefix, null), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<CreateProductApiKeyResponse> CreateKeyAsync(HttpClient admin, Guid productId, string kind)
    {
        using var response = await admin.PostAsJsonAsync($"/api/products/{productId}/api-keys", new CreateProductApiKeyRequest(kind, "Server"), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreateProductApiKeyResponse>(TestContext.Current.CancellationToken))!;
    }

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, HttpClient Admin, HttpClient Agent)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        await SignInAsync(admin);
        await SignInAsync(agent);
        return (factory, database, admin, agent);
    }

    [Fact]
    public async Task Only_the_prefix_and_hash_are_stored_and_the_plaintext_is_shown_once()
    {
        var (factory, database, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var product = await CreateProductAsync(admin, "orbitly", "ORB");

        var created = await CreateKeyAsync(admin, product.Id, "Trusted");

        var plaintext = created.PlaintextKey;
        var secretPart = plaintext[created.Key.KeyPrefix.Length..];
        var expectedHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
        (await database.ScalarAsync<string>($"SELECT key_hash FROM product_api_keys WHERE id = '{created.Key.Id}'")).ShouldBe(expectedHash);
        (await database.ScalarAsync<long>(
            $"SELECT count(*) FROM product_api_keys WHERE key_hash LIKE '%' || '{secretPart}' || '%' OR key_prefix = '{plaintext}'")).ShouldBe(0);
        var listJson = await admin.GetStringAsync($"/api/products/{product.Id}/api-keys", TestContext.Current.CancellationToken);
        listJson.ShouldContain(created.Key.KeyPrefix);
        listJson.ShouldNotContain(plaintext);
        listJson.ShouldNotContain("sha256");
        (await database.ScalarAsync<long>($"SELECT count(*) FROM admin_events WHERE payload::text LIKE '%{secretPart}%'")).ShouldBe(0);
        (await database.ScalarAsync<long>("SELECT count(*) FROM admin_events WHERE type = 'ApiKeyCreated'")).ShouldBe(1);
        factory.LogSink.Events.ShouldNotBeEmpty();
        factory.LogSink.Events.ShouldAllBe(e => !e.RenderMessage().Contains(plaintext) && !e.Properties.Values.Any(v => v.ToString().Contains(plaintext)));
        factory.LogSink.Events.ShouldAllBe(e => !e.RenderMessage().Contains(secretPart));
    }

    [Fact]
    public async Task The_create_response_is_not_cacheable()
    {
        var (factory, _, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var product = await CreateProductAsync(admin, "orbitly", "ORB");

        using var response = await admin.PostAsJsonAsync($"/api/products/{product.Id}/api-keys", new CreateProductApiKeyRequest("Trusted", "Server"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Both_kinds_can_be_created_and_revoked_per_product()
    {
        var (factory, _, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var product = await CreateProductAsync(admin, "orbitly", "ORB");
        var trusted = await CreateKeyAsync(admin, product.Id, "Trusted");
        var publicKey = await CreateKeyAsync(admin, product.Id, "Public");
        trusted.PlaintextKey.ShouldStartWith("tsk_");
        publicKey.PlaintextKey.ShouldStartWith("tsp_");

        using var revoke = await admin.DeleteAsync($"/api/products/{product.Id}/api-keys/{publicKey.Key.Id}", TestContext.Current.CancellationToken);

        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var keys = (await admin.GetFromJsonAsync<List<ProductApiKeyDto>>($"/api/products/{product.Id}/api-keys", TestContext.Current.CancellationToken))!;
        keys.Count.ShouldBe(2);
        keys.Single(k => k.Id == publicKey.Key.Id).RevokedAt.ShouldNotBeNull();
        keys.Single(k => k.Id == trusted.Key.Id).RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Revoking_a_key_of_another_product_is_404()
    {
        var (factory, _, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var first = await CreateProductAsync(admin, "orbitly", "ORB");
        var second = await CreateProductAsync(admin, "nimbus", "NIM");
        var key = await CreateKeyAsync(admin, first.Id, "Trusted");

        using var response = await admin.DeleteAsync($"/api/products/{second.Id}/api-keys/{key.Key.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var keys = (await admin.GetFromJsonAsync<List<ProductApiKeyDto>>($"/api/products/{first.Id}/api-keys", TestContext.Current.CancellationToken))!;
        keys.Single().RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task An_agent_cannot_list_or_create_keys()
    {
        var (factory, _, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var product = await CreateProductAsync(admin, "orbitly", "ORB");

        using var list = await agent.GetAsync($"/api/products/{product.Id}/api-keys", TestContext.Current.CancellationToken);
        using var create = await agent.PostAsJsonAsync($"/api/products/{product.Id}/api-keys", new CreateProductApiKeyRequest("Trusted", null), TestContext.Current.CancellationToken);

        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_key_revoked_through_the_api_is_401_on_the_next_intake_call()
    {
        var (factory, _, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var product = await CreateProductAsync(admin, "orbitly", "ORB");
        var key = await CreateKeyAsync(admin, product.Id, "Trusted");
        using var anonymous = factory.CreateClient();
        var submit = new SubmitTicketRequest("ada@example.com", "Ada", "Help", "Please help", null, null);

        async Task<HttpResponseMessage> SubmitAsync(string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/intake/tickets") { Content = JsonContent.Create(submit) };
            request.Headers.Add(HeaderNames.ApiKey, apiKey);
            return await anonymous.SendAsync(request, TestContext.Current.CancellationToken);
        }

        async Task<string> ShapeAsync(HttpResponseMessage response)
        {
            var ignored = new[] { "Date", "X-Correlation-Id", "X-Request-Id" };
            var headers = response.Headers.Concat(response.Content.Headers)
                .Where(h => !ignored.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .Select(h => $"{h.Key}={string.Join(",", h.Value)}");
            return $"{(int)response.StatusCode}|{await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}|{string.Join(";", headers)}";
        }

        using (var before = await SubmitAsync(key.PlaintextKey))
        {
            before.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using (var revoke = await admin.DeleteAsync($"/api/products/{product.Id}/api-keys/{key.Key.Id}", TestContext.Current.CancellationToken))
        {
            revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var after = await SubmitAsync(key.PlaintextKey);
        using var unknown = await SubmitAsync("tsk_not-a-real-key");
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ShapeAsync(after)).ShouldBe(await ShapeAsync(unknown));
    }

    [Fact(Timeout = 60_000)]
    public async Task Revoking_twice_is_204_both_times_and_audits_once()
    {
        var (factory, database, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var product = await CreateProductAsync(admin, "orbitly", "ORB");
        var key = await CreateKeyAsync(admin, product.Id, "Trusted");

        using var first = await admin.DeleteAsync($"/api/products/{product.Id}/api-keys/{key.Key.Id}", TestContext.Current.CancellationToken);
        using var second = await admin.DeleteAsync($"/api/products/{product.Id}/api-keys/{key.Key.Id}", TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await database.ScalarAsync<long>("SELECT count(*) FROM admin_events WHERE type = 'ApiKeyRevoked'")).ShouldBe(1);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Settings;

namespace TechStrap.Api.Tests.Settings;

public sealed class SiteSettingsEndpointTests(TestPostgres postgres)
{
    private async Task<(ApiFactory Factory, ApiTestDatabase Database, HttpClient Admin)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        (await admin.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        return (factory, database, admin);
    }

    [Fact(Timeout = 120_000)]
    public async Task An_admin_reads_and_changes_the_default_pack_and_anonymous_visitors_see_it_cached()
    {
        var (factory, database, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;

        var current = await admin.GetFromJsonAsync<SiteSettingsDto>("/api/settings/site", TestContext.Current.CancellationToken);
        current!.DefaultPack.ShouldBe("classic");

        using var put = await admin.PutAsJsonAsync("/api/settings/site", new UpdateSiteSettingsRequest("midnight", current.Version), TestContext.Current.CancellationToken);
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await put.Content.ReadFromJsonAsync<SiteSettingsDto>(TestContext.Current.CancellationToken))!.DefaultPack.ShouldBe("midnight");

        using var anonymous = factory.CreateClient();
        using var site = await anonymous.GetAsync("/api/public/site", TestContext.Current.CancellationToken);
        site.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await site.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("{\"defaultPack\":\"midnight\"}");
        site.Headers.CacheControl!.ToString().ShouldBe("public, max-age=300");
        site.Headers.Contains("Set-Cookie").ShouldBeFalse();
        (await database.ScalarAsync<long>("SELECT count(*) FROM admin_events WHERE type = 'SiteSettingsUpdated' AND payload::text LIKE '%midnight%'")).ShouldBe(1);
    }

    [Fact(Timeout = 120_000)]
    public async Task An_agent_is_403_on_both_admin_routes()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));

        using var get = await agent.GetAsync("/api/settings/site", TestContext.Current.CancellationToken);
        using var put = await agent.PutAsJsonAsync("/api/settings/site", new UpdateSiteSettingsRequest("slate", 1), TestContext.Current.CancellationToken);

        get.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        put.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact(Timeout = 120_000)]
    public async Task An_unknown_pack_is_400_on_default_pack_and_a_stale_version_is_409()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        var current = await admin.GetFromJsonAsync<SiteSettingsDto>("/api/settings/site", TestContext.Current.CancellationToken);

        using var unknown = await admin.PutAsJsonAsync("/api/settings/site", new UpdateSiteSettingsRequest("neon", current!.Version), TestContext.Current.CancellationToken);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("errorCodes").GetProperty("default-pack").EnumerateArray().Select(code => code.GetString()).ShouldBe(["skin-pack-unknown"]);

        using var stale = await admin.PutAsJsonAsync("/api/settings/site", new UpdateSiteSettingsRequest("slate", current.Version + 1000), TestContext.Current.CancellationToken);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}

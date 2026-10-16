using System.Net;
using System.Text.Json;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Settings;

namespace TechStrap.Admin.Tests.Clients;

public sealed class AppearanceSettingsClientTests
{
    [Fact]
    public async Task Read_and_save_use_existing_site_api_and_carry_the_version()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnJson(HttpMethod.Get, "/api/settings/site", new SiteSettingsDto("slate", 17));
        api.Stub.OnJson(HttpMethod.Put, "/api/settings/site", new SiteSettingsDto("paper", 18));
        var client = api.Get<IAppearanceSettingsClient>();
        (await client.GetAsync(TestContext.Current.CancellationToken)).Value.Version.ShouldBe(17u);
        var saved = await client.UpdateAsync(new("paper", 17), TestContext.Current.CancellationToken);
        saved.Value.DefaultPack.ShouldBe("paper");
        using var json = JsonDocument.Parse(api.Stub.Requests.Last().Body!);
        json.RootElement.GetProperty("version").GetUInt32().ShouldBe(17u);
        json.RootElement.GetProperty("defaultPack").GetString().ShouldBe("paper");
    }

    [Fact]
    public async Task A_conflict_is_returned_without_retrying_the_write()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnProblem(HttpMethod.Put, "/api/settings/site", HttpStatusCode.Conflict, "concurrency-conflict", "Changed.");
        var result = await api.Get<IAppearanceSettingsClient>().UpdateAsync(new("paper", 17), TestContext.Current.CancellationToken);
        result.Errors.Single().Code.ShouldBe("concurrency-conflict");
        api.Stub.Requests.ShouldHaveSingleItem();
    }
}

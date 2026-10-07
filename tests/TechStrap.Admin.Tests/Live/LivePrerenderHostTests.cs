using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Admin.Features.Live;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Live;

/// <summary>
/// The pages are prerendered (<c>InteractiveServer</c> with prerendering on): the first render runs on the HTTP request with no circuit. The real client is resolved there (the indicator and the pages inject it)
/// but must never open a connection: starting is allowed only from <c>OnAfterRenderAsync</c>, which prerendering does not run.
/// </summary>
public sealed class LivePrerenderHostTests
{
    [Theory(Timeout = 60000)]
    [InlineData("/")]
    [InlineData("/queue/mine")]
    public async Task Prerendering_a_page_opens_no_hub_connection(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var connections = new FakeLiveConnectionFactory();
        await using var factory = new AdminFactory(configureServices: services =>
        {
            services.RemoveAll<ILiveConnectionFactory>();
            services.AddSingleton<ILiveConnectionFactory>(connections);
        });
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var response = await client.GetAsync(path, ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("ts-shell");
        connections.Created.ShouldBeEmpty("a prerender must not start the live connection");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact(Timeout = 60000)]
    public async Task Switched_off_the_prerendered_page_carries_no_live_markup()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["LiveUpdates:Enabled"] = "false" });
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await (await client.GetAsync("/", ct)).Content.ReadAsStringAsync(ct);

        html.ShouldContain("ts-shell");
        html.ShouldNotContain("ts-live");
    }
}

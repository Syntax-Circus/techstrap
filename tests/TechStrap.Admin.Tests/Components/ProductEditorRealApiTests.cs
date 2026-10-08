using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Clients;
using TechStrap.Admin.Tests.Support;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The editor over the real <c>ApiConnection</c> and products client, stubbed only at the socket: the 400 the API answers (kebab-case field targets in <c>errorCodes</c>) must reach the right field,
/// and the JSON the editor sends must carry the flag and the version under the names the API reads.
/// </summary>
public sealed class ProductEditorRealApiTests : AdminPageTest
{
    [Fact]
    public async Task A_400_with_a_kebab_case_target_reaches_its_field_and_the_request_body_carries_isActive_and_version()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, $"/api/products/{TestData.OrbitlyId}", TestData.ProductDetail(version: 7));
        api.Stub.On(HttpMethod.Put, $"/api/products/{TestData.OrbitlyId}", _ =>
            StubApiHandler.ValidationProblem("accent-colour", "accent-colour-invalid", "Use a colour like #RRGGBB."));
        Services.AddSingleton(api.Get<IProductsClient>());
        var cut = Render<ProductEditorPage>(p => p.Add(c => c.Id, TestData.OrbitlyId.ToString()));
        cut.Find("#ts-product-name").Input("Orbitly Cloud");
        cut.Find("#ts-product-accent").Input("#FF0000");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find("#ts-product-accent-error").TextContent.ShouldBe("Use a colour like #RRGGBB."));
        cut.Find("#ts-product-name").GetAttribute("value").ShouldBe("Orbitly Cloud");
        var put = api.Stub.Requests.Single(r => r.Method == HttpMethod.Put);
        using var body = JsonDocument.Parse(put.Body!);
        body.RootElement.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        body.RootElement.GetProperty("version").GetUInt32().ShouldBe(7u);
        body.RootElement.GetProperty("name").GetString().ShouldBe("Orbitly Cloud");
        body.RootElement.GetProperty("branding").GetProperty("accentColour").GetString().ShouldBe("#FF0000");
    }

    [Fact]
    public async Task A_409_concurrency_conflict_from_the_api_raises_the_banner_and_keeps_the_form()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, $"/api/products/{TestData.OrbitlyId}", TestData.ProductDetail(version: 7));
        api.Stub.OnProblem(HttpMethod.Put, $"/api/products/{TestData.OrbitlyId}", HttpStatusCode.Conflict, ApiErrorCodes.ConcurrencyConflict, "This product changed since you opened it. Reload it and apply your change again.");
        Services.AddSingleton(api.Get<IProductsClient>());
        var cut = Render<ProductEditorPage>(p => p.Add(c => c.Id, TestData.OrbitlyId.ToString()));
        cut.Find("#ts-product-name").Input("Mine");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("This product changed since you opened it."));
        cut.Find("#ts-product-name").GetAttribute("value").ShouldBe("Mine");
    }

    [Fact]
    public async Task A_409_product_host_taken_from_the_api_shows_at_the_portal_host_field()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, $"/api/products/{TestData.OrbitlyId}", TestData.ProductDetail(version: 7));
        api.Stub.OnProblem(HttpMethod.Put, $"/api/products/{TestData.OrbitlyId}", HttpStatusCode.Conflict, ApiErrorCodes.ProductHostTaken, "Another product already uses this portal hostname.");
        Services.AddSingleton(api.Get<IProductsClient>());
        var cut = Render<ProductEditorPage>(p => p.Add(c => c.Id, TestData.OrbitlyId.ToString()));
        cut.Find("#ts-product-host").Input("support.orbitly.test");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find("#ts-product-host-error").TextContent.ShouldBe(ProductsCopy.PortalHostTaken));
    }

    [Fact]
    public async Task A_500_from_the_api_is_an_uncertain_save_not_a_failure()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, $"/api/products/{TestData.OrbitlyId}", TestData.ProductDetail(version: 7));
        api.Stub.OnProblem(HttpMethod.Put, $"/api/products/{TestData.OrbitlyId}", HttpStatusCode.InternalServerError, "internal-error", "An unexpected error occurred.");
        Services.AddSingleton(api.Get<IProductsClient>());
        var cut = Render<ProductEditorPage>(p => p.Add(c => c.Id, TestData.OrbitlyId.ToString()));
        cut.Find("#ts-product-name").Input("Mine");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("The save may have gone through."));
        api.Stub.Requests.Count(r => r.Method == HttpMethod.Put).ShouldBe(1, "a write is never retried");
    }
}

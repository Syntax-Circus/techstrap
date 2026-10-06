using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Products;

/// <summary>P09-T05: <c>/</c> sends visitors to the default product when one is configured; otherwise it is a neutral page with no product list, so nothing can be enumerated.</summary>
public sealed class RootPageHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Default(string? key) => new() { [PortalOptions.DefaultProductKey] = key };

    [Fact]
    public async Task With_a_default_product_the_root_redirects_with_a_302_to_its_home_and_calls_nothing()
    {
        await using var factory = new PortalFactory(settings: Default("paperplane"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location!.PathAndQuery.ShouldBe("/p/paperplane");
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_redirect_follows_through_to_the_themed_home()
    {
        await using var factory = new PortalFactory(settings: Default("paperplane"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", new TechStrap.Contracts.Products.PublicProductDto("paperplane", "Paperplane", null, "#F59E0B", "#000000", "#9D6507"));
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("How can we help?");
        html.ShouldContain("--ts-accent:#F59E0B");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_default_product_the_root_is_a_neutral_page_with_no_product_list_and_no_api_call(string? key)
    {
        await using var factory = new PortalFactory(settings: Default(key));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<h1>Support</h1>");
        html.ShouldContain("the link in your email");
        html.ShouldNotContain("<ul");
        html.ShouldNotContain("/p/");
        html.ShouldNotContain("--ts-accent");
        html.ShouldNotContain("ts-product");
        html.ShouldNotContain("TechStrap Portal");
        html.ShouldContain("ts-powered");
        factory.Api.Requests.ShouldBeEmpty("the root never asks the API which products exist");
    }

    [Fact]
    public async Task A_default_product_that_is_not_a_slug_stops_the_host_at_start()
    {
        await using var factory = new PortalFactory(settings: Default("Not A Slug"));

        Should.Throw<Microsoft.Extensions.Options.OptionsValidationException>(() => factory.CreateClient());
    }
}

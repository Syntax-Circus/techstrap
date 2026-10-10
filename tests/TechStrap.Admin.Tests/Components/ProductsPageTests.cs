using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

public sealed class ProductsPageTests : AdminPageTest
{
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();

    public ProductsPageTests()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<ProductDto>>(
        [
            TestData.ProductDetail("Orbitly", active: true, key: "orbitly", prefix: "ORB"),
            TestData.ProductDetail("Acme", id: Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a2"), active: false, key: "acme", prefix: "ACM"),
        ]));
        Services.AddSingleton(_products);
    }

    [Fact]
    public void An_admin_sees_every_product_by_name_with_its_key_prefix_and_status_in_words()
    {
        var cut = Render<ProductsPage>();

        var rows = cut.FindAll("tbody tr");
        rows.Select(r => r.QuerySelector("td a")!.TextContent).ShouldBe(["Acme", "Orbitly"]);
        rows[0].QuerySelectorAll(".ts-pill").Last().TextContent.ShouldBe("Inactive");
        rows[1].QuerySelectorAll(".ts-pill").Last().TextContent.ShouldBe("Active");
        rows[1].QuerySelectorAll("code").Select(c => c.TextContent).ShouldBe(["orbitly", "ORB"]);
    }

    [Fact]
    public void The_list_shows_each_products_portal_host_or_an_empty_cell()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>(
        [
            TestData.ProductDetail("Orbitly", key: "orbitly", prefix: "ORB", portalHost: "support.orbitly.test"),
            TestData.ProductDetail("Acme", id: Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a2"), key: "acme", prefix: "ACM"),
        ]));

        var cut = Render<ProductsPage>();

        cut.FindAll("thead th").Select(h => h.TextContent.Trim()).ShouldContain("Portal host");
        var orbitly = cut.Find("tr[data-product=orbitly]").QuerySelectorAll("td");
        orbitly[3].TextContent.ShouldBe("support.orbitly.test");
        orbitly[3].QuerySelector("code").ShouldNotBeNull();
        cut.Find("tr[data-product=acme]").QuerySelectorAll("td")[3].TextContent.ShouldBeEmpty();
    }

    [Fact]
    public void The_list_shows_whether_a_product_is_listed_on_the_landing_page()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>(
        [
            TestData.ProductDetail("Orbitly", key: "orbitly", prefix: "ORB"),
            TestData.ProductDetail("Acme", id: Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a2"), key: "acme", prefix: "ACM", listed: false),
        ]));

        var cut = Render<ProductsPage>();

        cut.FindAll("thead th").Select(h => h.TextContent.Trim()).ShouldContain(ProductsCopy.ColumnListed);
        cut.Find("tr[data-product=orbitly]").QuerySelectorAll("td")[4].TextContent.ShouldBe(ProductsCopy.Listed);
        cut.Find("tr[data-product=acme]").QuerySelectorAll("td")[4].TextContent.ShouldBe(ProductsCopy.Hidden);
    }

    [Fact]
    public void Each_row_links_to_its_editor_and_its_keys_and_the_header_links_to_a_new_product()
    {
        var cut = Render<ProductsPage>();

        var orbitly = cut.Find("tr[data-product=orbitly]");
        orbitly.QuerySelectorAll("td.ts-settings-actions a").Select(a => a.GetAttribute("href")).ShouldBe(
            [$"/settings/products/{TestData.OrbitlyId}", $"/settings/products/{TestData.OrbitlyId}/keys"]);
        cut.Find(".ts-settings-head a.btn").GetAttribute("href").ShouldBe("/settings/products/new");
    }

    [Fact]
    public void While_loading_it_shows_a_skeleton_and_then_the_rows()
    {
        var gate = new TaskCompletionSource<Result<IReadOnlyList<ProductDto>>>();
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);

        var cut = Render<ProductsPage>();

        cut.Find("[role=status]").TextContent.ShouldContain("Loading products");
        cut.FindAll("table").ShouldBeEmpty();
        gate.SetResult(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.ProductDetail()]));
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void No_products_is_a_plain_empty_state_with_the_next_step()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([]));

        var cut = Render<ProductsPage>();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No products yet");
        cut.Markup.ShouldContain("Create the first product");
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<ProductDto>>("api-error", "The API is unavailable."), TestData.Ok<IReadOnlyList<ProductDto>>([TestData.ProductDetail()]));
        var cut = Render<ProductsPage>();

        cut.Find("[role=alert]").TextContent.ShouldContain("Couldn't load products. The API is unavailable.");
        cut.Find("[role=alert] button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_the_api_is_not_asked()
    {
        AsAgent();

        var cut = Render<ProductsPage>();

        cut.Find("section.ts-no-access h1").TextContent.ShouldBe("You don't have access to this page.");
        cut.FindAll("table").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("New product");
        _products.ReceivedCalls().ShouldBeEmpty();
    }
}

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

public sealed class ProductKeysPageTests : AdminPageTest
{
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();

    public ProductKeysPageTests()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail()));
        _products.ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductApiKeyDto>>([TestData.ApiKey()]));
        Services.AddSingleton(_products);
    }

    private IRenderedComponent<ProductKeysPage> RenderPage() => Render<ProductKeysPage>(p => p.Add(c => c.Id, TestData.OrbitlyId));

    [Fact]
    public void An_admin_sees_the_product_name_a_link_back_and_its_keys()
    {
        var cut = RenderPage();

        cut.Find("h1").TextContent.ShouldBe("Orbitly");
        cut.Find(".ts-settings-back a").GetAttribute("href").ShouldBe("/settings/products");
        cut.Find(".ts-settings-head a").GetAttribute("href").ShouldBe($"/settings/products/{TestData.OrbitlyId}");
        cut.FindAll("tbody tr").Count.ShouldBe(1);
        cut.Find("form.ts-key-create").ShouldNotBeNull();
    }

    [Fact]
    public void A_product_the_api_does_not_know_says_so_and_asks_for_no_keys()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ProductNotFound, "No such product.", ResultErrorKind.NotFound));

        var cut = RenderPage();

        cut.Find("[role=alert]").TextContent.ShouldContain("This product no longer exists.");
        cut.FindAll("form.ts-key-create").ShouldBeEmpty();
        _products.DidNotReceive().ListApiKeysAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_nothing_is_asked_of_the_api()
    {
        AsAgent();

        var cut = RenderPage();

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        cut.FindAll("form.ts-key-create").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("tsk_");
        _products.ReceivedCalls().ShouldBeEmpty();
    }
}

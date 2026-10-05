using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// A page past the end moves to the last real page. Between the move being asked for and it landing (a real browser takes a round trip) the page must show the loading state and not a blank list;
/// bUnit's navigation lands at once, so this double records the move and never raises it.
/// </summary>
public sealed class KbArticleListRedirectTests : AdminComponentTest
{
    private sealed class HeldNavigation : NavigationManager
    {
        public HeldNavigation() => Initialize("http://localhost/", "http://localhost/kb?page=9");

        public List<string> Moves { get; } = [];

        protected override void NavigateToCore(string uri, NavigationOptions options) => Moves.Add(uri);
    }

    private readonly HeldNavigation _navigation = new();

    public KbArticleListRedirectTests()
    {
        var kb = Substitute.For<IKbClient>();
        kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([], page: 9, total: 30)));
        kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<KbCategoryDto>>([]));
        var products = Substitute.For<IProductsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([]));
        Services.AddSingleton(kb);
        Services.AddSingleton(products);
        Services.AddSingleton<NavigationManager>(_navigation);
    }

    [Fact]
    public void While_the_move_to_the_last_page_has_not_landed_the_page_shows_the_loading_state_and_not_a_blank_list()
    {
        var cut = Render<KbArticleListPage>();

        cut.WaitForAssertion(() => _navigation.Moves.ShouldBe(["/kb?page=2"]));
        cut.FindAll(".ts-empty, table, [role=alert]").ShouldBeEmpty();
        cut.Find(".ts-loading").TextContent.ShouldContain(KbCopy.Loading);
    }
}

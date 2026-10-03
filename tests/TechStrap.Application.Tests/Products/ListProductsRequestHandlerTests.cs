using NSubstitute;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Products;

public sealed class ListProductsRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly Product _product = Product.Restore(
        Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null), isActive: true, version: 3);

    [Fact]
    public async Task An_agent_lists_active_products_only()
    {
        _claims.Current.Returns(new AgentClaims("a", "Sam", "sam@example.com", AgentRole.Agent));
        _products.ListAsync(true, Arg.Any<CancellationToken>()).Returns([_product]);

        var result = await new ListProductsRequestHandler(_claims, _products).HandleAsync(TestContext.Current.CancellationToken);

        result.Value.ShouldHaveSingleItem().Version.ShouldBe(3u);
        await _products.Received(1).ListAsync(true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_admin_lists_every_product()
    {
        _claims.Current.Returns(new AgentClaims("a", "Sam", "sam@example.com", AgentRole.Admin));
        _products.ListAsync(false, Arg.Any<CancellationToken>()).Returns([_product]);

        var result = await new ListProductsRequestHandler(_claims, _products).HandleAsync(TestContext.Current.CancellationToken);

        result.Value.ShouldHaveSingleItem();
        await _products.Received(1).ListAsync(false, Arg.Any<CancellationToken>());
    }
}

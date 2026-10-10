using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Products;

public sealed class GetProductRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();

    private Product Stored(bool isActive)
    {
        var product = Product.Restore(
            Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null), isActive, version: 4);
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        return product;
    }

    private GetProductRequestHandler Handler(AgentRole role)
    {
        _claims.Current.Returns(new AgentClaims("a", "Sam", "sam@example.com", role));
        return new GetProductRequestHandler(_claims, _products, Substitute.For<IProductLogoUrls>());
    }

    [Fact]
    public async Task A_found_product_is_returned()
    {
        var product = Stored(isActive: true);

        var result = await Handler(AgentRole.Agent).HandleAsync(product.Id, TestContext.Current.CancellationToken);

        result.Value.ShouldSatisfyAllConditions(dto => dto.Id.ShouldBe(product.Id), dto => dto.Version.ShouldBe(4u));
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
        var result = await Handler(AgentRole.Agent).HandleAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task An_inactive_product_is_hidden_from_an_agent_but_not_an_admin()
    {
        var product = Stored(isActive: false);

        var asAgent = await Handler(AgentRole.Agent).HandleAsync(product.Id, TestContext.Current.CancellationToken);
        var asAdmin = await Handler(AgentRole.Admin).HandleAsync(product.Id, TestContext.Current.CancellationToken);

        asAgent.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
        asAdmin.Value.IsActive.ShouldBeFalse();
    }
}

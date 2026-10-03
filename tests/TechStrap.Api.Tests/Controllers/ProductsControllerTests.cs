using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Api.Controllers;
using TechStrap.Application.Products;
using TechStrap.Contracts.Products;

namespace TechStrap.Api.Tests.Controllers;

public sealed class ProductsControllerTests
{
    private static readonly ProductDto Product = new(
        Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1F6FEB", "#FFFFFF", "#1F6FEB", null, null), 5);

    [Fact]
    public async Task List_DelegatesAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<ProductDto> products = [Product];
        var handler = Substitute.For<IListProductsRequestHandler>();
        handler.HandleAsync(cancellation.Token).Returns(Result<IReadOnlyList<ProductDto>>.Success(products));

        var result = await ControllerTestContext.For<ProductsController>().List(handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(products);
        await handler.Received(1).HandleAsync(cancellation.Token);
    }

    [Fact]
    public async Task Get_DelegatesTheIdAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<IGetProductRequestHandler>();
        handler.HandleAsync(Product.Id, cancellation.Token).Returns(Result<ProductDto>.Success(Product));

        var result = await ControllerTestContext.For<ProductsController>().Get(Product.Id, handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(Product);
        await handler.Received(1).HandleAsync(Product.Id, cancellation.Token);
    }

    [Fact]
    public async Task Create_DelegatesAndReturnsCreatedAtGet()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new CreateProductRequest("orbitly", "Orbitly", "ORB", null);
        var handler = Substitute.For<ICreateProductRequestHandler>();
        handler.HandleAsync(request, cancellation.Token).Returns(Result<ProductDto>.Success(Product));

        var result = await ControllerTestContext.For<ProductsController>().Create(request, handler, cancellation.Token);

        result.ShouldBeOfType<CreatedAtActionResult>().ActionName.ShouldBe("Get");
        await handler.Received(1).HandleAsync(request, cancellation.Token);
    }

    [Fact]
    public async Task Update_DelegatesTheIdAndBodyAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", null, null, null, null), true, 5);
        var handler = Substitute.For<IUpdateProductRequestHandler>();
        handler.HandleAsync(Product.Id, request, cancellation.Token).Returns(Result<ProductDto>.Success(Product));

        var result = await ControllerTestContext.For<ProductsController>().Update(Product.Id, request, handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>();
        await handler.Received(1).HandleAsync(Product.Id, request, cancellation.Token);
    }
}

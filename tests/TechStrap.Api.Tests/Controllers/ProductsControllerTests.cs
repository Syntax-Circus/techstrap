using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Api.Controllers;
using TechStrap.Application.ApiKeys;
using TechStrap.Application.Products;
using TechStrap.Contracts.ApiKeys;
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

    private static readonly ProductApiKeyDto ApiKey = new(Guid.CreateVersion7(), Product.Id, "Trusted", "tsk_abcdefgh", null, DateTimeOffset.UnixEpoch, null, null);

    [Fact]
    public async Task ListApiKeys_DelegatesTheProductIdAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<ProductApiKeyDto> keys = [ApiKey];
        var handler = Substitute.For<IListProductApiKeysRequestHandler>();
        handler.HandleAsync(Product.Id, cancellation.Token).Returns(Result<IReadOnlyList<ProductApiKeyDto>>.Success(keys));

        var result = await ControllerTestContext.For<ProductsController>().ListApiKeys(Product.Id, handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(keys);
        await handler.Received(1).HandleAsync(Product.Id, cancellation.Token);
    }

    [Fact]
    public async Task CreateApiKey_DelegatesAndReturns201WithTheKey()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new CreateProductApiKeyRequest("Trusted", null);
        var response = new CreateProductApiKeyResponse(ApiKey, "tsk_secret");
        var handler = Substitute.For<ICreateProductApiKeyRequestHandler>();
        handler.HandleAsync(Product.Id, request, cancellation.Token).Returns(Result<CreateProductApiKeyResponse>.Success(response));

        var result = await ControllerTestContext.For<ProductsController>().CreateApiKey(Product.Id, request, handler, cancellation.Token);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(201);
        objectResult.Value.ShouldBe(response);
        await handler.Received(1).HandleAsync(Product.Id, request, cancellation.Token);
    }

    [Fact]
    public async Task RevokeApiKey_DelegatesTheIdsAndReturnsNoContent()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<IRevokeProductApiKeyRequestHandler>();
        handler.HandleAsync(Product.Id, ApiKey.Id, cancellation.Token).Returns(Result.Success());

        var result = await ControllerTestContext.For<ProductsController>().RevokeApiKey(Product.Id, ApiKey.Id, handler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>();
        await handler.Received(1).HandleAsync(Product.Id, ApiKey.Id, cancellation.Token);
    }
}

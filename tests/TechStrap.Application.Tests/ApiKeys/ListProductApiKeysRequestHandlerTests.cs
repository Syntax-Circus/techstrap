using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.ApiKeys;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.ApiKeys;

public sealed class ListProductApiKeysRequestHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Product _product = Product.Create("orbitly", "Orbitly", "ORB", null, new FakeTimeProvider()).Value;

    [Fact]
    public async Task Keys_map_to_dtos_with_the_revocation_time_and_no_hash()
    {
        var revokedAt = _clock.GetUtcNow().AddHours(-1);
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(_product);
        _products.ListApiKeysAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(
        [
            ProductApiKey.Restore(Guid.CreateVersion7(), _product.Id, ApiKeyKind.Public, "tsp_aaaaaaaa", "sha256:aaaa", "App", _clock.GetUtcNow(), null, null),
            ProductApiKey.Restore(Guid.CreateVersion7(), _product.Id, ApiKeyKind.Trusted, "tsk_bbbbbbbb", "sha256:bbbb", null, _clock.GetUtcNow(), revokedAt, null),
        ]);

        var result = await new ListProductApiKeysRequestHandler(_products).HandleAsync(_product.Id, TestContext.Current.CancellationToken);

        result.Value.Select(k => k.RevokedAt).ShouldBe([null, revokedAt]);
        result.Value.Select(k => k.Kind).ShouldBe(["Public", "Trusted"]);
        var json = JsonSerializer.Serialize(result.Value);
        json.ShouldNotContain("sha256");
        json.ShouldNotContain("KeyHash");
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
        var result = await new ListProductApiKeysRequestHandler(_products).HandleAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }
}

using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Application.Tests.Support;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Products;

public sealed class UploadProductLogoRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly IProductLogoStore _store = Substitute.For<IProductLogoStore>();
    private readonly IProductLogoUrls _logoUrls = Substitute.For<IProductLogoUrls>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _admin;
    private readonly Product _product;

    public UploadProductLogoRequestHandlerTests()
    {
        _admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(_admin);
        _product = Product.Restore(
            Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null), isActive: true, version: 7);
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(_product);
    }

    private UploadProductLogoRequestHandler Handler(params Result[] commits) =>
        new(_claims, _agents, _products, _store, _logoUrls, _events, UnitOfWorkSubstitute.Create(commits), _clock);

    [Fact]
    public async Task An_upload_stores_the_file_sets_the_name_audits_and_deletes_the_previous_file_after_commit()
    {
        _product.SetUploadedLogo("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png");
        _store.SaveAsync(Arg.Any<IncomingProductLogo>(), Arg.Any<CancellationToken>())
            .Returns(Result<StoredProductLogo>.Success(new StoredProductLogo("product-logos/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", "image/webp", 10)));
        _logoUrls.UrlFor("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp").Returns("https://api.test/product-logos/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp");

        var result = await Handler().HandleAsync(_product.Id, new IncomingProductLogo(10, new MemoryStream(new byte[10])), TestContext.Current.CancellationToken);

        result.Value.Branding.UploadedLogoUrl.ShouldBe("https://api.test/product-logos/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp");
        _product.Branding.UploadedLogo.ShouldBe("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp");
        _products.Received(1).Update(_product);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.Type == AdminEventType.ProductUpdated && e.PayloadJson == "{\"changed\":[\"uploadedLogo\"]}"));
        await _store.Received(1).DeleteAsync("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png", Arg.Any<CancellationToken>());
        await _store.DidNotReceive().DeleteAsync("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_commit_deletes_the_new_file_and_keeps_the_previous_name()
    {
        _product.SetUploadedLogo("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png");
        _store.SaveAsync(Arg.Any<IncomingProductLogo>(), Arg.Any<CancellationToken>())
            .Returns(Result<StoredProductLogo>.Success(new StoredProductLogo("product-logos/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", "image/webp", 10)));

        var result = await Handler(Result.Failure(new ResultError("db-down", "down", ResultErrorKind.Failure))).HandleAsync(_product.Id, new IncomingProductLogo(10, new MemoryStream(new byte[10])), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        await _store.Received(1).DeleteAsync("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", Arg.Any<CancellationToken>());
        await _store.DidNotReceive().DeleteAsync("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_file_is_file_required_and_a_store_refusal_is_passed_through_before_anything_changes()
    {
        (await Handler().HandleAsync(_product.Id, null, TestContext.Current.CancellationToken)).Errors[0].Code.ShouldBe("file-required");
        _store.SaveAsync(Arg.Any<IncomingProductLogo>(), Arg.Any<CancellationToken>())
            .Returns(Result<StoredProductLogo>.Failure(new ResultError("product-logo-type-not-allowed", "no", ResultErrorKind.Validation, "file")));

        var refused = await Handler().HandleAsync(_product.Id, new IncomingProductLogo(3, new MemoryStream(new byte[3])), TestContext.Current.CancellationToken);

        refused.Errors[0].Code.ShouldBe("product-logo-type-not-allowed");
        _products.DidNotReceive().Update(Arg.Any<Product>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task An_unknown_product_is_not_found_and_nothing_is_stored()
    {
        var result = await Handler().HandleAsync(Guid.NewGuid(), new IncomingProductLogo(3, new MemoryStream(new byte[3])), TestContext.Current.CancellationToken);

        result.Errors[0].Code.ShouldBe("product-not-found");
        await _store.DidNotReceive().SaveAsync(Arg.Any<IncomingProductLogo>(), Arg.Any<CancellationToken>());
    }
}

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

public sealed class RemoveProductLogoRequestHandlerTests
{
    private const string Stored = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png";

    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly IProductLogoStore _store = Substitute.For<IProductLogoStore>();
    private readonly IProductLogoUrls _logoUrls = Substitute.For<IProductLogoUrls>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Product _product;

    public RemoveProductLogoRequestHandlerTests()
    {
        var admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(admin);
        _product = Product.Restore(
            Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null), isActive: true, version: 7);
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(_product);
    }

    private RemoveProductLogoRequestHandler Handler(params Result[] commits) =>
        new(_claims, _agents, _products, _store, _logoUrls, _events, UnitOfWorkSubstitute.Create(commits), _clock);

    [Fact]
    public async Task Removing_clears_the_name_audits_and_deletes_the_file_after_commit()
    {
        _product.SetUploadedLogo(Stored);

        var result = await Handler().HandleAsync(_product.Id, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Branding.UploadedLogoUrl.ShouldBeNull();
        _product.Branding.UploadedLogo.ShouldBeNull();
        _products.Received(1).Update(_product);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.Type == AdminEventType.ProductUpdated && e.PayloadJson == "{\"changed\":[\"uploadedLogo\"]}"));
        await _store.Received(1).DeleteAsync(Stored, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_when_there_is_no_uploaded_logo_is_a_no_op_200()
    {
        var result = await Handler().HandleAsync(_product.Id, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _products.DidNotReceive().Update(Arg.Any<Product>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
        await _store.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
        var result = await Handler().HandleAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Errors[0].Code.ShouldBe("product-not-found");
    }
}

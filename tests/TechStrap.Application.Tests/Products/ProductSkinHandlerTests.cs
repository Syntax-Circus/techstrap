using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Agents;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Skins;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;
using TechStrap.Domain.Settings;

namespace TechStrap.Application.Tests.Products;

public sealed class ProductSkinHandlerTests
{
    private static readonly ProductBrandingRequest SameBranding = new("Orbitly", null, "#1F6FEB", null, null);

    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly ISiteSettingsRepository _site = Substitute.For<ISiteSettingsRepository>();
    private readonly IProductLogoUrls _logoUrls = Substitute.For<IProductLogoUrls>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly PortalLinkOptions _portal = new() { PublicUrl = "https://help.test/" };
    private readonly Agent _admin;
    private readonly Product _product;
    private Product? _added;

    public ProductSkinHandlerTests()
    {
        _admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(_admin);
        _site.GetAsync(Arg.Any<CancellationToken>()).Returns(SiteSettings.Restore("classic", 1));
        _product = Product.Restore(
            Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null), isActive: true, version: 7);
        _products.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => _added?.Id == call.Arg<Guid>() ? _added : _product);
        _products.When(p => p.Add(Arg.Any<Product>())).Do(call => _added = call.Arg<Product>());
    }

    private UpdateProductRequestHandler Handler() =>
        new(_claims, _agents, _products, _events, UnitOfWorkSubstitute.Create(), Options.Create(_portal), _logoUrls, _site, _clock);

    private CreateProductRequestHandler CreateHandler() =>
        new(_claims, _agents, _products, _events, UnitOfWorkSubstitute.Create(), Options.Create(_portal), _logoUrls, _site, _clock);

    private static UpdateProductRequest Update(ProductSkin? skin) => new("Orbitly", SameBranding, true, 7, null, null, skin);

    [Fact]
    public async Task A_null_skin_leaves_the_stored_skin_and_is_not_audited()
    {
        _product.SetSkinJson("{\"pack\":\"slate\"}");

        var result = await Handler().HandleAsync(_product.Id, new UpdateProductRequest("Orbitly Cloud", SameBranding, true, 7), TestContext.Current.CancellationToken);

        result.Value.Skin!.Pack.ShouldBe("slate");
        _product.SkinJson.ShouldBe("{\"pack\":\"slate\"}");
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.PayloadJson == "{\"changed\":[\"name\"]}"));
    }

    [Fact]
    public async Task A_new_skin_is_stored_as_json_and_audited()
    {
        var result = await Handler().HandleAsync(_product.Id, Update(new ProductSkin(Pack: "paper", Brand: "#112233")), TestContext.Current.CancellationToken);

        _product.SkinJson.ShouldBe("{\"pack\":\"paper\",\"brand\":\"#112233\"}");
        result.Value.Skin!.Brand.ShouldBe("#112233");
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.PayloadJson == "{\"changed\":[\"skin\"]}"));
    }

    [Fact]
    public async Task An_empty_skin_clears_it()
    {
        _product.SetSkinJson("{\"pack\":\"slate\"}");

        var result = await Handler().HandleAsync(_product.Id, Update(new ProductSkin()), TestContext.Current.CancellationToken);

        _product.SkinJson.ShouldBeNull();
        result.Value.Skin.ShouldBeNull();
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.PayloadJson == "{\"changed\":[\"skin\"]}"));
    }

    [Fact]
    public async Task Resending_the_stored_skin_changes_and_audits_nothing()
    {
        _product.SetSkinJson("{\"pack\":\"slate\"}");

        var result = await Handler().HandleAsync(_product.Id, Update(new ProductSkin(Pack: "slate")), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
        _products.DidNotReceive().Update(Arg.Any<Product>());
    }

    [Theory]
    [InlineData("pack", "nope")]
    public async Task An_unknown_pack_is_skin_invalid_on_the_pack_token(string target, string value)
    {
        var result = await Handler().HandleAsync(_product.Id, Update(new ProductSkin(Pack: value)), TestContext.Current.CancellationToken);

        result.Errors[0].Code.ShouldBe("skin-invalid");
        result.Errors[0].Target.ShouldBe(target);
        _products.DidNotReceive().Update(Arg.Any<Product>());
    }

    [Fact]
    public async Task A_low_contrast_pair_is_skin_contrast_invalid_and_names_the_pair()
    {
        var result = await Handler().HandleAsync(_product.Id, Update(new ProductSkin(Ink: "#EEEEEE")), TestContext.Current.CancellationToken);

        result.Errors[0].Code.ShouldBe("skin-contrast-invalid");
        result.Errors[0].Target.ShouldBe("ink/background");
        _products.DidNotReceive().Update(Arg.Any<Product>());
    }

    [Fact]
    public async Task Contrast_is_judged_against_the_current_default_pack()
    {
        _site.GetAsync(Arg.Any<CancellationToken>()).Returns(SiteSettings.Restore("midnight", 1));

        // Dark ink is fine on Classic but unreadable on the Midnight background.
        var result = await Handler().HandleAsync(_product.Id, Update(new ProductSkin(Ink: "#222222")), TestContext.Current.CancellationToken);

        result.Errors[0].Code.ShouldBe("skin-contrast-invalid");
    }

    [Fact]
    public async Task Create_stores_a_valid_skin_and_none_when_null()
    {
        var none = await CreateHandler().HandleAsync(new CreateProductRequest("orbitly", "Orbitly", "ORB", null), TestContext.Current.CancellationToken);

        none.Value.Skin.ShouldBeNull();
        _added!.SkinJson.ShouldBeNull();
        _added = null;

        var request = new CreateProductRequest("orbitly", "Orbitly", "ORB", null, Skin: new ProductSkin(Pack: "contrast"));
        var with = await CreateHandler().HandleAsync(request, TestContext.Current.CancellationToken);

        _added!.SkinJson.ShouldBe("{\"pack\":\"contrast\"}");
        with.Value.Skin!.Pack.ShouldBe("contrast");
    }

    [Fact]
    public async Task Create_refuses_an_invalid_skin_and_stores_nothing()
    {
        var request = new CreateProductRequest("orbitly", "Orbitly", "ORB", null, Skin: new ProductSkin(Background: "red"));

        var result = await CreateHandler().HandleAsync(request, TestContext.Current.CancellationToken);

        result.Errors[0].Code.ShouldBe("skin-invalid");
        result.Errors[0].Target.ShouldBe("background");
        _products.DidNotReceive().Add(Arg.Any<Product>());
    }
}

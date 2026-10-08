using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Products;

public sealed class UpdateProductRequestHandlerTests
{
    private static readonly ProductBrandingRequest SameBranding = new("Orbitly", null, "#1F6FEB", null, null);

    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _admin;
    private readonly Product _product;

    public UpdateProductRequestHandlerTests()
    {
        _admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(_admin);
        _product = Product.Restore(
            Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null), isActive: true, version: 7);
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(_product);
    }

    private UpdateProductRequestHandler Handler(params Result[] commits) =>
        new(_claims, _agents, _products, _events, UnitOfWorkSubstitute.Create(commits), _clock);

    [Fact]
    public async Task An_update_changes_name_branding_and_status_and_audits_what_changed()
    {
        var request = new UpdateProductRequest("Orbitly Cloud", new ProductBrandingRequest("Orbitly Cloud", null, "#7c3aed", null, null), false, 7);

        var result = await Handler().HandleAsync(_product.Id, request, TestContext.Current.CancellationToken);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Name.ShouldBe("Orbitly Cloud"),
            dto => dto.IsActive.ShouldBeFalse(),
            dto => dto.Branding.AccentColour.ShouldBe("#7C3AED"),
            dto => dto.Branding.DisplayName.ShouldBe("Orbitly Cloud"));
        _products.Received(1).Update(_product);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.ProductUpdated && e.ActorId == _admin.Id && e.PayloadJson == "{\"changed\":[\"name\",\"branding\",\"isActive\"]}"));
    }

    private Product StoreWithHost(string? host)
    {
        var stored = Product.Restore(
            _product.Id, "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null), isActive: true, version: 7, portalHost: host);
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(stored);
        return stored;
    }

    [Fact]
    public async Task The_portal_host_is_normalised_and_checked_against_other_products_only()
    {
        var request = new UpdateProductRequest("Orbitly", SameBranding, true, 7, " Support.Example.COM ");

        var result = await Handler().HandleAsync(_product.Id, request, TestContext.Current.CancellationToken);

        result.Value.PortalHost.ShouldBe("support.example.com");
        await _products.Received(1).IsPortalHostTakenAsync("support.example.com", _product.Id, Arg.Any<CancellationToken>());
        _products.Received(1).Update(_product);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.PayloadJson == "{\"changed\":[\"portalHost\"]}"));
    }

    [Fact]
    public async Task Keeping_the_products_own_host_is_not_a_conflict_and_not_a_change()
    {
        var stored = StoreWithHost("support.example.com");
        _products.IsPortalHostTakenAsync("support.example.com", stored.Id, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Handler().HandleAsync(stored.Id, new UpdateProductRequest("Orbitly", SameBranding, true, 7, "SUPPORT.example.com"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PortalHost.ShouldBe("support.example.com");
        _products.DidNotReceive().Update(Arg.Any<Product>());
    }

    [Fact]
    public async Task A_null_portal_host_clears_it()
    {
        var stored = StoreWithHost("support.example.com");

        var result = await Handler().HandleAsync(stored.Id, new UpdateProductRequest("Orbitly", SameBranding, true, 7, null), TestContext.Current.CancellationToken);

        result.Value.PortalHost.ShouldBeNull();
        stored.PortalHost.ShouldBeNull();
        _products.Received(1).Update(stored);
        await _products.DidNotReceive().IsPortalHostTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_malformed_portal_host_is_a_field_error_reported_before_the_repository_is_asked()
    {
        var result = await Handler().HandleAsync(
            _product.Id, new UpdateProductRequest("Orbitly", SameBranding, true, 7, "support.example.com/path"), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("product-host-invalid"),
            error => error.Target.ShouldBe("portal-host"));
        await _products.DidNotReceive().IsPortalHostTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        _products.DidNotReceive().Update(Arg.Any<Product>());
    }

    [Fact]
    public async Task A_portal_host_used_by_another_product_is_a_conflict_and_nothing_is_saved()
    {
        _products.IsPortalHostTakenAsync("support.example.com", _product.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler().HandleAsync(
            _product.Id, new UpdateProductRequest("Orbitly", SameBranding, true, 7, "Support.Example.com"), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("product-host-taken"));
        _product.PortalHost.ShouldBeNull();
        _products.DidNotReceive().Update(Arg.Any<Product>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task A_stale_version_is_a_conflict_and_nothing_is_saved()
    {
        var result = await Handler().HandleAsync(_product.Id, new UpdateProductRequest("Orbitly Cloud", SameBranding, true, 6), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict));
        _products.DidNotReceive().Update(Arg.Any<Product>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task A_malformed_accent_is_a_field_error()
    {
        var request = new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", null, "purple", null, null), true, 7);

        var result = await Handler().HandleAsync(_product.Id, request, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("accent-colour"));
        _products.DidNotReceive().Update(Arg.Any<Product>());
    }

    private void StoreRelativeLogo() =>
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(Product.Restore(
            _product.Id, "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", "/images/old-logo.png", "#1F6FEB", null, null), isActive: true, version: 7));

    [Fact]
    public async Task A_product_with_a_relative_logo_from_before_the_rule_can_be_renamed_with_the_logo_left_as_it_is()
    {
        StoreRelativeLogo();
        var request = new UpdateProductRequest("Orbitly Cloud", new ProductBrandingRequest("Orbitly", "/images/old-logo.png", "#1F6FEB", null, null), true, 7);

        var result = await Handler().HandleAsync(_product.Id, request, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Orbitly Cloud");
        result.Value.Branding.LogoPath.ShouldBe("/images/old-logo.png");
        _products.Received(1).Update(Arg.Any<Product>());
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.PayloadJson == "{\"changed\":[\"name\"]}"));
    }

    [Fact]
    public async Task Changing_the_relative_logo_to_another_unsafe_address_is_still_a_field_error()
    {
        StoreRelativeLogo();
        var request = new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", "/images/other.png", "#1F6FEB", null, null), true, 7);

        var result = await Handler().HandleAsync(_product.Id, request, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("logo-path"),
            error => error.Code.ShouldBe("logo-path-invalid"));
        _products.DidNotReceive().Update(Arg.Any<Product>());
    }

    [Fact]
    public async Task A_stored_product_with_a_good_logo_still_refuses_a_relative_one()
    {
        var request = new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", "/images/logo.png", "#1F6FEB", null, null), true, 7);

        var result = await Handler().HandleAsync(_product.Id, request, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("logo-path-invalid");
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
        var result = await Handler().HandleAsync(Guid.CreateVersion7(), new UpdateProductRequest("Orbitly", SameBranding, true, 7), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task An_unknown_product_with_an_invalid_body_is_not_found_not_a_validation_error()
    {
        var request = new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", "javascript:alert(1)", "#1F6FEB", null, null), true, 7);

        var result = await Handler().HandleAsync(Guid.CreateVersion7(), request, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task A_conflict_at_commit_is_returned()
    {
        var result = await Handler(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ConcurrencyConflict))
            .HandleAsync(_product.Id, new UpdateProductRequest("Orbitly Cloud", SameBranding, true, 7), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task An_update_that_changes_nothing_is_not_audited()
    {
        var result = await Handler().HandleAsync(_product.Id, new UpdateProductRequest("Orbitly", SameBranding, true, 7), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
        _products.DidNotReceive().Update(Arg.Any<Product>());
    }

    [Fact]
    public async Task A_deactivated_actor_is_refused_before_any_lookup_or_staging()
    {
        var inactive = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        inactive.SetActive(false);
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(inactive);

        var request = new UpdateProductRequest("Orbitly Cloud", new ProductBrandingRequest("Orbitly Cloud", null, "purple", null, null), false, 7);

        var result = await Handler().HandleAsync(_product.Id, request, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        _ = _products.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _products.DidNotReceive().Update(Arg.Any<Product>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }
}

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

public sealed class CreateProductRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _admin;
    private Product? _added;

    public CreateProductRequestHandlerTests()
    {
        _admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(_admin);
        _products.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => _added?.Id == call.Arg<Guid>() ? _added : null);
        _products.When(p => p.Add(Arg.Any<Product>())).Do(call => _added = call.Arg<Product>());
    }

    private CreateProductRequestHandler Handler(params Result[] commits) =>
        new(_claims, _agents, _products, _events, UnitOfWorkSubstitute.Create(commits), _clock);

    [Fact]
    public async Task A_product_is_created_with_branding_and_audited_without_personal_data()
    {
        var result = await Handler().HandleAsync(
            new CreateProductRequest("orbitly", "Orbitly", "ORB", new ProductBrandingRequest("Orbitly", null, "#7c3aed", "support@orbitly.example", null)),
            TestContext.Current.CancellationToken);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Key.ShouldBe("orbitly"),
            dto => dto.Branding.AccentColour.ShouldBe("#7C3AED"),
            dto => dto.Branding.OnAccentColour.ShouldBe("#FFFFFF"),
            dto => dto.Branding.AccentInkColour.ShouldNotBeNullOrWhiteSpace());
        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.ProductCreated && e.ActorId == _admin.Id && e.PayloadJson == "{\"productKey\":\"orbitly\",\"numberPrefix\":\"ORB\"}"));
    }

    [Fact]
    public async Task Without_branding_the_default_branding_comes_from_the_name()
    {
        var result = await Handler().HandleAsync(new CreateProductRequest("orbitly", "Orbitly", "ORB", null), TestContext.Current.CancellationToken);

        result.Value.Branding.DisplayName.ShouldBe("Orbitly");
        result.Value.Branding.AccentColour.ShouldBe(ProductBranding.DefaultAccentColour);
    }

    [Fact]
    public async Task A_malformed_accent_is_a_field_error()
    {
        var result = await Handler().HandleAsync(
            new CreateProductRequest("orbitly", "Orbitly", "ORB", new ProductBrandingRequest("Orbitly", null, "purple", null, null)),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("accent-colour"));
        _products.DidNotReceive().Add(Arg.Any<Product>());
    }

    [Fact]
    public async Task A_taken_key_or_prefix_is_a_clear_conflict()
    {
        var result = await Handler(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.Duplicate))
            .HandleAsync(new CreateProductRequest("orbitly", "Orbitly", "ORB", null), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("product-key-taken"));
    }

    [Fact]
    public async Task The_portal_host_is_trimmed_lower_cased_and_stored()
    {
        var result = await Handler().HandleAsync(
            new CreateProductRequest("orbitly", "Orbitly", "ORB", null, " Support.Dragonpoop.COM "), TestContext.Current.CancellationToken);

        result.Value.PortalHost.ShouldBe("support.dragonpoop.com");
        _added!.PortalHost.ShouldBe("support.dragonpoop.com");
        await _products.Received(1).IsPortalHostTakenAsync("support.dragonpoop.com", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_blank_portal_host_is_stored_as_null_and_the_repository_is_not_asked()
    {
        var result = await Handler().HandleAsync(new CreateProductRequest("orbitly", "Orbitly", "ORB", null, "  "), TestContext.Current.CancellationToken);

        result.Value.PortalHost.ShouldBeNull();
        await _products.DidNotReceive().IsPortalHostTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_malformed_portal_host_is_a_field_error_reported_before_the_repository_is_asked()
    {
        var result = await Handler().HandleAsync(
            new CreateProductRequest("orbitly", "Orbitly", "ORB", null, "https://support.example.com"), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("product-host-invalid"),
            error => error.Target.ShouldBe("portal-host"));
        await _products.DidNotReceive().IsPortalHostTakenAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        _products.DidNotReceive().Add(Arg.Any<Product>());
    }

    [Fact]
    public async Task A_portal_host_used_by_another_product_is_a_conflict_and_nothing_is_staged()
    {
        _products.IsPortalHostTakenAsync("support.example.com", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler().HandleAsync(
            new CreateProductRequest("orbitly", "Orbitly", "ORB", null, "Support.Example.com"), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("product-host-taken"));
        _products.DidNotReceive().Add(Arg.Any<Product>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task A_deactivated_actor_is_refused_before_the_request_is_validated_and_nothing_is_staged()
    {
        var inactive = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        inactive.SetActive(false);
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(inactive);

        var result = await Handler().HandleAsync(
            new CreateProductRequest("orbitly", "Orbitly", "ORB", new ProductBrandingRequest("Orbitly", null, "purple", null, null)),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        _products.DidNotReceive().Add(Arg.Any<Product>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
        _ = _products.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}

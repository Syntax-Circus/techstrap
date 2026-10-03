using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.ApiKeys;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.ApiKeys;

public sealed class CreateProductApiKeyRequestHandlerTests
{
    private const string Plaintext = "tsk_abcdefghijABCDEFGHIJabcdefghijABCDEFGHIJabc";
    private const string Prefix = "tsk_abcdefgh";

    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly IApiKeyHasher _hasher = Substitute.For<IApiKeyHasher>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Product _product = Product.Create("orbitly", "Orbitly", "ORB", null, new FakeTimeProvider()).Value;

    public CreateProductApiKeyRequestHandlerTests()
    {
        var admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(admin);
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(_product);
        _hasher.Generate(Arg.Any<ApiKeyKind>()).Returns(new GeneratedApiKey(Plaintext, Prefix, "sha256:feed"));
    }

    private CreateProductApiKeyRequestHandler Handler() => new(_claims, _agents, _products, _events, _hasher, UnitOfWorkSubstitute.Create(), _clock);

    [Theory]
    [InlineData("Trusted", ApiKeyKind.Trusted)]
    [InlineData("public", ApiKeyKind.Public)]
    public async Task The_plaintext_appears_only_in_the_create_response(string kind, ApiKeyKind expected)
    {
        var result = await Handler().HandleAsync(_product.Id, new CreateProductApiKeyRequest(kind, "Server"), TestContext.Current.CancellationToken);

        result.Value.PlaintextKey.ShouldBe(Plaintext);
        result.Value.Key.KeyPrefix.ShouldBe(Prefix);
        _hasher.Received(1).Generate(expected);
        _products.Received(1).AddApiKey(Arg.Is<ProductApiKey>(k => k.KeyHash == "sha256:feed" && k.Kind == expected));
    }

    [Fact]
    public async Task The_audit_event_holds_the_prefix_but_no_secret_or_hash()
    {
        await Handler().HandleAsync(_product.Id, new CreateProductApiKeyRequest("Trusted", null), TestContext.Current.CancellationToken);

        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.ApiKeyCreated
            && e.PayloadJson.Contains(Prefix)
            && !e.PayloadJson.Contains(Plaintext)
            && !e.PayloadJson.Contains("sha256")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Secret")]
    public async Task An_unknown_kind_is_a_field_error(string? kind)
    {
        var result = await Handler().HandleAsync(_product.Id, new CreateProductApiKeyRequest(kind, null), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("kind"));
        _hasher.DidNotReceive().Generate(Arg.Any<ApiKeyKind>());
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
        (await Handler().HandleAsync(Guid.CreateVersion7(), new CreateProductApiKeyRequest("Public", null), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }
}

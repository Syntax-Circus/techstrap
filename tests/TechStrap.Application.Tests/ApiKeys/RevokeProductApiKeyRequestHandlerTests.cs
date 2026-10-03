using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.ApiKeys;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.ApiKeys;

public sealed class RevokeProductApiKeyRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Guid _productId = Guid.CreateVersion7();

    public RevokeProductApiKeyRequestHandlerTests()
    {
        var admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(admin);
    }

    private RevokeProductApiKeyRequestHandler Handler() => new(_claims, _agents, _products, _events, UnitOfWorkSubstitute.Create(), _clock);

    private ProductApiKey StoredKey(Guid productId, DateTimeOffset? revokedAt = null)
    {
        var key = ProductApiKey.Restore(Guid.CreateVersion7(), productId, ApiKeyKind.Trusted, "tsk_abcdefgh", "sha256:feed", null, _clock.GetUtcNow(), revokedAt, null);
        _products.GetApiKeyAsync(key.Id, Arg.Any<CancellationToken>()).Returns(key);
        return key;
    }

    [Fact]
    public async Task Revoking_an_active_key_saves_it_and_audits_the_prefix_only()
    {
        var key = StoredKey(_productId);

        var result = await Handler().HandleAsync(_productId, key.Id, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        key.IsRevoked.ShouldBeTrue();
        _products.Received(1).UpdateApiKey(key);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.ApiKeyRevoked
            && e.PayloadJson.Contains("tsk_abcdefgh")
            && e.PayloadJson.Contains(_productId.ToString())
            && !e.PayloadJson.Contains("sha256")));
    }

    [Fact]
    public async Task Revoking_an_already_revoked_key_succeeds_without_saving_or_auditing()
    {
        var key = StoredKey(_productId, _clock.GetUtcNow().AddDays(-1));

        var result = await Handler().HandleAsync(_productId, key.Id, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _products.DidNotReceive().UpdateApiKey(Arg.Any<ProductApiKey>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task A_key_of_another_product_is_not_found()
    {
        var key = StoredKey(Guid.CreateVersion7());

        var result = await Handler().HandleAsync(_productId, key.Id, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.NotFound),
            error => error.Code.ShouldBe("api-key-not-found"));
        key.IsRevoked.ShouldBeFalse();
    }

    [Fact]
    public async Task An_unknown_key_is_not_found()
    {
        var result = await Handler().HandleAsync(_productId, Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("api-key-not-found");
    }

    [Fact]
    public async Task A_deactivated_actor_is_refused_before_any_lookup_or_staging()
    {
        var inactive = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        inactive.SetActive(false);
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(inactive);

        var key = StoredKey(_productId);
        _products.ClearReceivedCalls();

        var result = await Handler().HandleAsync(_productId, key.Id, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        _ = _products.DidNotReceive().GetApiKeyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _products.DidNotReceive().UpdateApiKey(Arg.Any<ProductApiKey>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
        key.IsRevoked.ShouldBeFalse();
    }
}

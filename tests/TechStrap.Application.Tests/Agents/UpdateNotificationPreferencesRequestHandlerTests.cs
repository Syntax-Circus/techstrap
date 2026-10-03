using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Agents;

public sealed class UpdateNotificationPreferencesRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly Agent _me = Agent.Create("me", "Riley", "riley@example.com", AgentRole.Agent, new FakeTimeProvider()).Value;
    private readonly Product _orbitly = Product.Create("orbitly", "Orbitly", "ORB", null, new FakeTimeProvider()).Value;

    public UpdateNotificationPreferencesRequestHandlerTests()
    {
        _claims.Current.Returns(new AgentClaims("me", "Riley", "riley@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("me", Arg.Any<CancellationToken>()).Returns(_me);
        _products.GetByIdAsync(_orbitly.Id, Arg.Any<CancellationToken>()).Returns(_orbitly);
    }

    private UpdateNotificationPreferencesRequestHandler Handler() => new(_claims, _agents, _products, UnitOfWorkSubstitute.Create());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Enabling_and_disabling_stage_the_preference_for_the_caller(bool notify)
    {
        var result = await Handler().HandleAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(_orbitly.Id, notify)]), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _agents.Received(1).SetNotificationPreferenceAsync(new AgentNotificationPreference(_me.Id, _orbitly.Id, notify), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_product_is_a_field_error_and_nothing_is_saved()
    {
        var result = await Handler().HandleAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(_orbitly.Id, true), new NotificationPreferenceUpdateDto(Guid.CreateVersion7(), true)]),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("notification-product-unknown"),
            error => error.Target.ShouldBe("preferences[1].productId"));
        await _agents.DidNotReceive().SetNotificationPreferenceAsync(Arg.Any<AgentNotificationPreference>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_same_product_twice_is_a_field_error()
    {
        var result = await Handler().HandleAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(_orbitly.Id, true), new NotificationPreferenceUpdateDto(_orbitly.Id, false)]),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("notification-product-repeated");
    }

    [Fact]
    public async Task A_missing_list_is_a_field_error()
    {
        (await Handler().HandleAsync(new UpdateNotificationPreferencesRequest(null!), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Target.ShouldBe("preferences");
    }
}

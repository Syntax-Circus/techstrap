using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;

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
        _products.GetExistingIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlySet<Guid>)call.Arg<IReadOnlyCollection<Guid>>().Where(id => id == _orbitly.Id).ToHashSet());
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

    [Fact(Timeout = 60_000)]
    public async Task Products_are_checked_with_one_repository_call()
    {
        var ids = Enumerable.Range(0, 30).Select(_ => Guid.CreateVersion7()).ToArray();
        _products.GetExistingIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>)ids.ToHashSet());

        var result = await Handler().HandleAsync(
            new UpdateNotificationPreferencesRequest(ids.Select(id => new NotificationPreferenceUpdateDto(id, true)).ToList()), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _products.Received(1).GetExistingIdsAsync(Arg.Is<IReadOnlyCollection<Guid>>(asked => asked.Count == 30 && ids.All(asked.Contains)), Arg.Any<CancellationToken>());
        await _products.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = 60_000)]
    public async Task More_than_the_cap_is_refused_before_any_lookup()
    {
        var entries = Enumerable.Range(0, DomainLimits.NotificationPreferencesMaxCount + 1)
            .Select(_ => new NotificationPreferenceUpdateDto(Guid.CreateVersion7(), true)).ToList();

        var result = await Handler().HandleAsync(new UpdateNotificationPreferencesRequest(entries), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("notification-preferences-too-many"),
            error => error.Target.ShouldBe("preferences"));
        await _products.DidNotReceive().GetExistingIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await _agents.DidNotReceive().SetNotificationPreferenceAsync(Arg.Any<AgentNotificationPreference>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_cap_leaves_room_for_the_full_set_the_Admin_page_saves_on_every_toggle()
    {
        // The Admin page sends one entry per active product; the cap is only a pre-lookup bound, so it must stay well above a plausible catalog.
        DomainLimits.NotificationPreferencesMaxCount.ShouldBeGreaterThanOrEqualTo(2000);
    }

    [Fact(Timeout = 60_000)]
    public async Task Exactly_the_cap_is_accepted()
    {
        var ids = Enumerable.Range(0, DomainLimits.NotificationPreferencesMaxCount).Select(_ => Guid.CreateVersion7()).ToArray();
        _products.GetExistingIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>)ids.ToHashSet());

        var result = await Handler().HandleAsync(
            new UpdateNotificationPreferencesRequest(ids.Select(id => new NotificationPreferenceUpdateDto(id, true)).ToList()), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
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
    public async Task A_repeat_is_reported_before_an_earlier_unknown_product_because_list_shape_errors_precede_lookups()
    {
        var unknown = Guid.CreateVersion7();
        var result = await Handler().HandleAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(unknown, true), new NotificationPreferenceUpdateDto(unknown, false)]),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("notification-product-repeated"),
            error => error.Target.ShouldBe("preferences[1].productId"));
    }

    [Fact]
    public async Task A_missing_list_is_a_field_error()
    {
        (await Handler().HandleAsync(new UpdateNotificationPreferencesRequest(null!), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Target.ShouldBe("preferences");
    }

    [Fact]
    public async Task A_null_preference_element_is_a_field_error()
    {
        var result = await Handler().HandleAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(_orbitly.Id, true), null!]), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("notification-preference-required"),
            error => error.Target.ShouldBe("preferences[1]"));
        await _agents.DidNotReceive().SetNotificationPreferenceAsync(Arg.Any<AgentNotificationPreference>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_deactivated_agent_cannot_change_preferences()
    {
        _me.SetActive(false);

        var result = await Handler().HandleAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(Guid.CreateVersion7(), true)]), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        await _agents.DidNotReceive().SetNotificationPreferenceAsync(Arg.Any<AgentNotificationPreference>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Applying_the_same_request_twice_succeeds_both_times()
    {
        var request = new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(_orbitly.Id, true)]);
        var handler = Handler();

        (await handler.HandleAsync(request, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        (await handler.HandleAsync(request, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        await _agents.Received(2).SetNotificationPreferenceAsync(new AgentNotificationPreference(_me.Id, _orbitly.Id, true), Arg.Any<CancellationToken>());
    }
}

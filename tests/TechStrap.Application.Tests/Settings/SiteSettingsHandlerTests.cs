using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Settings;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Settings;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Settings;

namespace TechStrap.Application.Tests.Settings;

public sealed class SiteSettingsHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly ISiteSettingsRepository _site = Substitute.For<ISiteSettingsRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _admin;

    public SiteSettingsHandlerTests()
    {
        _admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(_admin);
        _site.GetAsync(Arg.Any<CancellationToken>()).Returns(_ => SiteSettings.Restore("classic", 4));
    }

    private UpdateSiteSettingsRequestHandler Update() => new(_claims, _agents, _site, _events, UnitOfWorkSubstitute.Create(), _clock);

    [Fact]
    public async Task Get_returns_the_default_pack_and_version()
    {
        var result = await new GetSiteSettingsRequestHandler(_claims, _agents, _site).HandleAsync(TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new SiteSettingsDto("classic", 4));
    }

    [Fact]
    public async Task Get_refuses_a_caller_without_claims()
    {
        _claims.Current.Returns((AgentClaims?)null);

        var result = await new GetSiteSettingsRequestHandler(_claims, _agents, _site).HandleAsync(TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-access-required");
    }

    [Fact]
    public async Task Update_stores_the_pack_and_audits_it()
    {
        var result = await Update().HandleAsync(new UpdateSiteSettingsRequest("slate", 4), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _site.Received(1).Update(Arg.Is<SiteSettings>(s => s.DefaultPackKey == "slate"));
        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.SiteSettingsUpdated && e.ActorId == _admin.Id && e.PayloadJson == "{\"defaultPack\":\"slate\"}"));
    }

    [Fact]
    public async Task An_unknown_pack_is_skin_pack_unknown_on_default_pack()
    {
        var result = await Update().HandleAsync(new UpdateSiteSettingsRequest("neon", 4), TestContext.Current.CancellationToken);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("skin-pack-unknown");
        error.Target.ShouldBe("default-pack");
        _site.DidNotReceive().Update(Arg.Any<SiteSettings>());
    }

    [Fact]
    public async Task A_stale_version_is_a_concurrency_conflict()
    {
        var result = await Update().HandleAsync(new UpdateSiteSettingsRequest("slate", 3), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        _site.DidNotReceive().Update(Arg.Any<SiteSettings>());
    }

    [Fact]
    public async Task Update_refuses_a_deactivated_actor()
    {
        _admin.SetActive(false);

        var result = await Update().HandleAsync(new UpdateSiteSettingsRequest("slate", 4), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task Resending_the_current_pack_changes_and_audits_nothing()
    {
        var result = await Update().HandleAsync(new UpdateSiteSettingsRequest("classic", 4), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _site.DidNotReceive().Update(Arg.Any<SiteSettings>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task The_public_site_carries_the_pack_only()
    {
        _site.GetAsync(Arg.Any<CancellationToken>()).Returns(SiteSettings.Restore("midnight", 9));

        var result = await new GetPublicSiteRequestHandler(_site).HandleAsync(TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new PublicSiteDto("midnight"));
    }
}

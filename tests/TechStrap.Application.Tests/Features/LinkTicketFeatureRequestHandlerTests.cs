using NSubstitute;
using TechStrap.Application.Features;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Knowledge;
using TechStrap.Application.Tests.Support;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Features;
using TechStrap.Domain.Features;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Features;

public sealed class LinkTicketFeatureRequestHandlerTests
{
    private readonly KbFixture _f = new();
    private readonly IFeatureRepository _features = Substitute.For<IFeatureRepository>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private LinkTicketFeatureRequestHandler Handler() => new(_f.Claims, _f.Agents, _tickets, _features, UnitOfWorkSubstitute.Create(), _f.Clock);
    private Ticket Ticket()
    {
        var t = TicketBuilder.WithVersion(TicketBuilder.InStatus(TicketStatus.Open, _f.Clock), 5);
        t.ChangeType(TicketType.FeatureRequest, Actor.ForAgent(_f.Agent.Id), _f.Clock);
        _tickets.GetByIdAsync(t.Id, Arg.Any<CancellationToken>()).Returns(t);
        return t;
    }
    [Fact]

    public async Task Linking_to_another_products_feature_is_rejected()
    {
        var t = Ticket();
        var f = Feature.Create(Guid.NewGuid(), "feature", "Title", "Details", _f.Clock).Value;
        _features.GetAsync(f.Id, Arg.Any<CancellationToken>()).Returns(f);
        var r = await Handler().HandleAsync(t.Id, new(f.Id, 5), TestContext.Current.CancellationToken);
        r.Errors[0].Code.ShouldBe("feature-product-mismatch");
        t.FeatureId.ShouldBeNull();
    }
    [Theory]
    [InlineData(null)]
    [InlineData(4u)]

    public async Task Missing_or_stale_ticket_versions_cannot_link(uint? version)
    {
        var t = Ticket();
        var r = await Handler().HandleAsync(t.Id, new(Guid.NewGuid(), version), TestContext.Current.CancellationToken);
        r.IsFailure.ShouldBeTrue();
        t.FeatureId.ShouldBeNull();
    }
    [Fact]

    public async Task An_unknown_feature_is_a_field_error()
    {
        var t = Ticket();
        var r = await Handler().HandleAsync(t.Id, new(Guid.NewGuid(), 5), TestContext.Current.CancellationToken);
        r.Errors[0].Target.ShouldBe("featureId");
    }
}

using TechStrap.Admin.Features.Live;
using TechStrap.Contracts.Live;
using static TechStrap.Admin.Tests.Live.LiveTestData;

namespace TechStrap.Admin.Tests.Live;

public sealed class LiveChangeRulesTests
{
    [Fact]
    public void A_change_by_the_signed_in_agent_is_own_and_every_other_change_is_not()
    {
        LiveChangeRules.IsOwn(Change(actor: MeId), MeId).ShouldBeTrue();
        LiveChangeRules.IsOwn(Change(actor: ColleagueId), MeId).ShouldBeFalse();
        LiveChangeRules.IsOwn(Change(actor: Guid.Empty), MeId).ShouldBeFalse();
        LiveChangeRules.IsOwn(Change(actor: MeId), null).ShouldBeFalse();
    }

    [Fact]
    public void A_system_change_has_no_actor_and_is_never_own()
    {
        var system = Change() with { ActorAgentId = null };

        LiveChangeRules.IsOwn(system, MeId).ShouldBeFalse();
    }

    [Fact]
    public void A_resync_is_never_own_even_when_it_somehow_names_the_agent()
    {
        LiveChangeRules.IsOwn(Resync(), MeId).ShouldBeFalse();
        LiveChangeRules.IsOwn(Resync() with { ActorAgentId = MeId }, MeId).ShouldBeFalse();
        LiveChangeRules.IsResync(Resync()).ShouldBeTrue();
        LiveChangeRules.IsResync(Change()).ShouldBeFalse();
    }

    [Fact]
    public void A_change_concerns_a_ticket_when_it_names_it_or_asks_for_a_full_reload()
    {
        LiveChangeRules.Concerns(Change(ticketId: TicketId), TicketId).ShouldBeTrue();
        LiveChangeRules.Concerns(Change(ticketId: OtherTicketId), TicketId).ShouldBeFalse();
        LiveChangeRules.Concerns(Resync(), TicketId).ShouldBeTrue();
    }

    [Fact]
    public void The_synthetic_resync_carries_the_wire_shape_of_the_servers()
    {
        var resync = LiveChangeRules.NewResync(At);

        resync.Kind.ShouldBe(TicketChangeKinds.Resync);
        resync.TicketId.ShouldBe(Guid.Empty);
        resync.ProductId.ShouldBe(Guid.Empty);
        resync.TicketNumber.ShouldBeEmpty();
        resync.EventType.ShouldBeEmpty();
        resync.ActorAgentId.ShouldBeNull();
        resync.EventId.ShouldNotBe(Guid.Empty);
        resync.OccurredAt.ShouldBe(At);
    }
}

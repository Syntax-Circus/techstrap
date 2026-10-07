using TechStrap.Admin.Features.Live;
using TechStrap.Contracts.Live;
using static TechStrap.Admin.Tests.Live.LiveTestData;

namespace TechStrap.Admin.Tests.Live;

/// <summary>The pure part of the presence bar: who is shown, in which order, with which words. The agent never sees themselves.</summary>
public sealed class PresenceViewModelFactoryTests
{
    private static readonly Guid Ada = Guid.Parse("0197f2a0-0000-7000-8000-0000000000a1");
    private static readonly Guid Bo = Guid.Parse("0197f2a0-0000-7000-8000-0000000000a2");
    private static readonly Guid Cy = Guid.Parse("0197f2a0-0000-7000-8000-0000000000a3");

    private static string[] Texts(TicketPresenceDto? presence, Guid? self = null, bool composingExpired = false) =>
        [.. PresenceViewModelFactory.Create(presence, self, composingExpired).Select(v => v.Text)];

    [Fact]
    public void Nobody_else_gives_an_empty_list()
    {
        PresenceViewModelFactory.Create(null, MeId).ShouldBeEmpty();
        PresenceViewModelFactory.Create(Presence(TicketId), MeId).ShouldBeEmpty();
    }

    [Fact]
    public void Viewers_are_shown_as_viewing()
    {
        Texts(Presence(TicketId, Viewer(Ada, "Ada Admin"))).ShouldBe(["Ada Admin is viewing"]);
    }

    [Fact]
    public void A_composing_viewer_is_shown_as_replying()
    {
        var models = PresenceViewModelFactory.Create(Presence(TicketId, Viewer(Ada, "Ada Admin", TicketPresenceStates.Composing)), null);

        models.ShouldHaveSingleItem().Text.ShouldBe("Ada Admin is replying");
        models[0].IsReplying.ShouldBeTrue();
        models[0].AgentId.ShouldBe(Ada);
    }

    [Fact]
    public void Both_kinds_list_the_repliers_first_then_each_group_by_name_ignoring_case()
    {
        var presence = Presence(
            TicketId,
            Viewer(Cy, "cy cole"),
            Viewer(Bo, "Bo Bell", TicketPresenceStates.Composing),
            Viewer(Ada, "Ada Admin"),
            Viewer(Guid.Parse("0197f2a0-0000-7000-8000-0000000000a4"), "Abe Aldrin", TicketPresenceStates.Composing));

        Texts(presence).ShouldBe(["Abe Aldrin is replying", "Bo Bell is replying", "Ada Admin is viewing", "cy cole is viewing"]);
    }

    [Fact]
    public void The_agent_themself_is_never_listed_whatever_their_state()
    {
        var presence = Presence(TicketId, Viewer(MeId, "Sam Ortiz", TicketPresenceStates.Composing), Viewer(Ada, "Ada Admin"));

        Texts(presence, MeId).ShouldBe(["Ada Admin is viewing"]);
        Texts(Presence(TicketId, Viewer(MeId, "Sam Ortiz")), MeId).ShouldBeEmpty();
        Texts(Presence(TicketId, Viewer(MeId, "Sam Ortiz")), null).ShouldBe(["Sam Ortiz is viewing"]);
    }

    [Fact]
    public void A_lapsed_composing_hint_is_shown_as_viewing()
    {
        var presence = Presence(TicketId, Viewer(Ada, "Ada Admin", TicketPresenceStates.Composing));

        Texts(presence, MeId, composingExpired: true).ShouldBe(["Ada Admin is viewing"]);
    }

    [Fact]
    public void An_agent_listed_twice_is_shown_once_and_replying_wins()
    {
        var presence = Presence(TicketId, Viewer(Ada, "Ada Admin"), Viewer(Ada, "Ada Admin", TicketPresenceStates.Composing));

        Texts(presence).ShouldBe(["Ada Admin is replying"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_replaced_by_a_neutral_one(string name)
    {
        Texts(Presence(TicketId, Viewer(Ada, name))).ShouldBe([PresenceCopy.AnotherAgent + " is viewing"]);
    }
}

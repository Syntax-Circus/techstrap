using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Tickets;

public sealed class TicketStatusTransitionTests
{
    private static readonly HashSet<(TicketStatus From, TicketStatus To)> AllowedPairs =
    [
        (TicketStatus.New, TicketStatus.Open),
        (TicketStatus.New, TicketStatus.Pending),
        (TicketStatus.New, TicketStatus.Solved),
        (TicketStatus.Open, TicketStatus.Pending),
        (TicketStatus.Open, TicketStatus.Solved),
        (TicketStatus.Pending, TicketStatus.Open),
        (TicketStatus.Pending, TicketStatus.Solved),
        (TicketStatus.Solved, TicketStatus.Open),
        (TicketStatus.Solved, TicketStatus.Closed),
    ];

    public static TheoryData<TicketStatus, TicketStatus, bool> AllPairs()
    {
        var data = new TheoryData<TicketStatus, TicketStatus, bool>();
        foreach (var from in Enum.GetValues<TicketStatus>())
        {
            foreach (var to in Enum.GetValues<TicketStatus>())
            {
                data.Add(from, to, AllowedPairs.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Every_status_pair_follows_the_transition_table(TicketStatus from, TicketStatus to, bool expected)
    {
        TicketStatusRules.CanTransition(from, to).ShouldBe(expected);
    }

    [Fact]
    public void Nothing_leaves_Closed_and_Closed_is_read_only()
    {
        TicketStatusRules.AllowedFrom(TicketStatus.Closed).ShouldBeEmpty();
        TicketStatusRules.IsReadOnly(TicketStatus.Closed).ShouldBeTrue();
        TicketStatusRules.IsReadOnly(TicketStatus.Solved).ShouldBeFalse();
    }

    [Fact]
    public void The_status_enum_has_exactly_the_five_documented_values()
    {
        Enum.GetNames<TicketStatus>().ShouldBe(["New", "Open", "Pending", "Solved", "Closed"]);
    }

    [Fact]
    public void AllowedFrom_returns_a_read_only_view_that_cannot_change_the_rules()
    {
        var allowed = TicketStatusRules.AllowedFrom(TicketStatus.New);

        allowed.ShouldNotBeOfType<TicketStatus[]>();
        allowed.ShouldBe([TicketStatus.Open, TicketStatus.Pending, TicketStatus.Solved]);
        ((ICollection<TicketStatus>)allowed).IsReadOnly.ShouldBeTrue();
        TicketStatusRules.AllowedFrom(TicketStatus.Closed).ShouldBeEmpty();
        TicketStatusRules.CanTransition(TicketStatus.New, TicketStatus.Closed).ShouldBeFalse();
    }
}

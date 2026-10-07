using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Live;

internal static class LiveTestData
{
    /// <summary>The ticket of <c>TestData.Detail()</c>, so a change for it concerns the page the component tests open.</summary>
    public static readonly Guid TicketId = TestData.TicketId;

    public static readonly Guid OtherTicketId = Guid.Parse("0197f2a0-0000-7000-8000-000000000009");

    /// <summary>The agent of <c>AgentSessions.SignedIn()</c>.</summary>
    public static readonly Guid MeId = AgentSessions.SamId;

    public static readonly Guid ColleagueId = Guid.Parse("0197f2a0-0000-7000-8000-0000000000aa");
    public static readonly DateTimeOffset At = new(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);

    public static TicketChangedDto Change(Guid? ticketId = null, Guid? actor = null, Guid? eventId = null, string kind = TicketChangeKinds.Updated) =>
        new(eventId ?? Guid.NewGuid(), ticketId ?? TicketId, "ORB-42", Guid.NewGuid(), TicketEventTypes.StatusChanged, actor ?? ColleagueId, At, kind);

    public static TicketChangedDto Resync() =>
        new(Guid.NewGuid(), Guid.Empty, string.Empty, Guid.Empty, string.Empty, null, At, TicketChangeKinds.Resync);

    public static TicketPresenceDto Presence(Guid ticketId, params TicketViewerDto[] viewers) => new(ticketId, viewers);

    public static TicketViewerDto Viewer(Guid agentId, string name, string state = TicketPresenceStates.Viewing) => new(agentId, name, state);
}

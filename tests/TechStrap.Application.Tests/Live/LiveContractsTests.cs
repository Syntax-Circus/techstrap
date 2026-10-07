using System.Text.Json;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Live;

/// <summary>
/// The live-update wire contract: the DTOs survive a System.Text.Json round trip with the web (camelCase) defaults every host uses, the names that cross
/// the process boundary are pinned, and the string constants stay in step with the enums they mirror.
/// </summary>
public sealed class LiveContractsTests
{
    private static readonly Guid TicketId = Guid.Parse("0197f2a0-0000-7000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("0197f2a0-0000-7000-8000-000000000002");
    private static readonly Guid ProductId = Guid.Parse("0197f2a0-0000-7000-8000-000000000003");
    private static readonly Guid AgentId = Guid.Parse("0197f2a0-0000-7000-8000-000000000004");
    private static readonly DateTimeOffset At = new(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonSerializerOptions.Web), JsonSerializerOptions.Web)!;

    [Fact]
    public void A_ticket_change_round_trips_and_is_camel_case()
    {
        var dto = new TicketChangedDto(EventId, TicketId, "ORB-42", ProductId, TicketEventTypes.StatusChanged, AgentId, At, TicketChangeKinds.Updated);

        RoundTrip(dto).ShouldBe(dto);
        var json = JsonSerializer.Serialize(dto, JsonSerializerOptions.Web);
        json.ShouldContain("\"eventId\":");
        json.ShouldContain("\"ticketNumber\":\"ORB-42\"");
        json.ShouldContain("\"actorAgentId\":");
        json.ShouldContain("\"kind\":\"Updated\"");
    }

    [Fact]
    public void A_ticket_change_without_an_actor_round_trips_with_a_null_actor()
    {
        var dto = new TicketChangedDto(EventId, TicketId, "ORB-42", ProductId, TicketEventTypes.StatusChanged, null, At, TicketChangeKinds.Updated);

        RoundTrip(dto).ActorAgentId.ShouldBeNull();
    }

    [Fact]
    public void A_ticket_change_carries_ids_and_names_only()
    {
        // The payload never holds customer text (D-018): every property is an id, a wire name, a ticket number or a time.
        typeof(TicketChangedDto).GetProperties().Select(property => property.Name).ShouldBe(
            ["EventId", "TicketId", "TicketNumber", "ProductId", "EventType", "ActorAgentId", "OccurredAt", "Kind"]);
    }

    [Fact]
    public void Presence_round_trips_with_its_viewers()
    {
        var dto = new TicketPresenceDto(TicketId, [new TicketViewerDto(AgentId, "Sam", TicketPresenceStates.Composing)]);

        var copy = RoundTrip(dto);

        copy.TicketId.ShouldBe(TicketId);
        copy.Viewers.ShouldBe(dto.Viewers);
        JsonSerializer.Serialize(dto, JsonSerializerOptions.Web).ShouldContain("\"displayName\":\"Sam\"");
    }

    [Fact]
    public void The_request_records_round_trip()
    {
        RoundTrip(new RelayTicketChangeRequest("{}")).ShouldBe(new RelayTicketChangeRequest("{}"));
        var presence = new UpdateTicketPresenceRequest(TicketPresenceActions.SetComposing, "sam", "conn-1", TicketId, true);
        RoundTrip(presence).ShouldBe(presence);
    }

    [Fact]
    public void The_names_that_cross_the_process_boundary_are_pinned()
    {
        TicketHubRoutes.Path.ShouldBe("/hubs/tickets");
        TicketHubMethods.JoinTicket.ShouldBe("JoinTicket");
        TicketHubMethods.LeaveTicket.ShouldBe("LeaveTicket");
        TicketHubMethods.SetComposing.ShouldBe("SetComposing");
        TicketHubMethods.TicketChanged.ShouldBe("TicketChanged");
        TicketHubMethods.PresenceChanged.ShouldBe("PresenceChanged");
        TicketHubGroups.Queue.ShouldBe("queue");
        TicketHubGroups.Ticket(TicketId).ShouldBe("ticket:0197f2a0-0000-7000-8000-000000000001");
        TicketChangeKinds.Created.ShouldBe("Created");
        TicketChangeKinds.Updated.ShouldBe("Updated");
        TicketChangeKinds.Resync.ShouldBe("Resync");
        TicketLiveLimits.ComposingTtlSeconds.ShouldBe(10);
        TicketLiveLimits.MaxChangePayloadBytes.ShouldBe(2048);
    }

    [Fact]
    public void Every_constant_in_a_group_is_distinct()
    {
        foreach (var holder in new[] { typeof(TicketHubMethods), typeof(TicketChangeKinds), typeof(TicketPresenceStates), typeof(TicketPresenceActions) })
        {
            var values = holder.GetFields().Where(field => field.IsLiteral && field.FieldType == typeof(string)).Select(field => (string)field.GetRawConstantValue()!).ToList();
            values.Distinct().Count().ShouldBe(values.Count, holder.Name);
        }
    }

    [Fact]
    public void The_viewer_states_match_the_wire_names()
    {
        Enum.GetNames<TicketViewerState>().ShouldBe(
            [TicketPresenceStates.Viewing, TicketPresenceStates.Composing], ignoreOrder: true);
    }

    [Fact]
    public void A_change_maps_to_its_dto_and_back()
    {
        var change = new TicketChange(EventId, TicketId, "ORB-42", ProductId, TicketEventTypes.Assigned, AgentId, At, TicketChangeKinds.Created);

        var dto = change.ToDto();

        dto.ShouldBe(new TicketChangedDto(EventId, TicketId, "ORB-42", ProductId, TicketEventTypes.Assigned, AgentId, At, TicketChangeKinds.Created));
        TicketChange.From(dto).ShouldBe(change);
    }

    [Fact]
    public void Presence_maps_to_its_dto_with_wire_state_names()
    {
        var presence = new TicketPresence(TicketId, [new TicketViewer(AgentId, "Sam", TicketViewerState.Composing), new TicketViewer(EventId, "Kim", TicketViewerState.Viewing)]);

        var dto = presence.ToDto();

        dto.TicketId.ShouldBe(TicketId);
        dto.Viewers.ShouldBe(
            [new TicketViewerDto(AgentId, "Sam", TicketPresenceStates.Composing), new TicketViewerDto(EventId, "Kim", TicketPresenceStates.Viewing)]);
    }

    [Fact]
    public void A_resync_names_no_ticket()
    {
        var resync = TicketChange.Resync(EventId, At);

        resync.Kind.ShouldBe(TicketChangeKinds.Resync);
        resync.TicketId.ShouldBe(Guid.Empty);
        resync.EventId.ShouldBe(EventId);
        resync.OccurredAt.ShouldBe(At);
    }

    [Fact]
    public void The_event_type_names_are_the_domain_ones()
    {
        // A TicketChange.EventType is a TicketEventTypes wire name, which TicketNamesParityTests already ties to the domain enum.
        Enum.TryParse<TicketEventType>(TicketEventTypes.ProductChanged, out _).ShouldBeTrue();
    }
}

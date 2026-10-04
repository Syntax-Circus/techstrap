using System.Globalization;
using System.Text.Json;
using TechStrap.Admin.Components.Ui;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// Builds the single chronological stream from the API's messages and events (both oldest first). A <c>MessageAdded</c> event is folded into its message
/// (matched by <c>messageId</c>), so a reply appears once. Payloads carry ids only; names come from <see cref="TicketLookups"/>. An event type this build does not know,
/// or a payload that cannot be read, becomes a generic line: the timeline never throws (PHASE-07 T09).
/// </summary>
public static class TimelineEntryFactory
{
    private const string MessageIdKey = "messageId";
    private const string ChannelKey = "channel";
    private const string FromKey = "from";
    private const string ToKey = "to";
    private const string TagIdKey = "tagId";
    private const string ReasonKey = "reason";
    private const string IsSpamKey = "isSpam";
    private const string TagDeletedReason = "tag-deleted";

    private const int CreatedRank = 0;
    private const int MessageRank = 1;
    private const int EventRank = 2;

    public static IReadOnlyList<TimelineEntryViewModel> Build(IReadOnlyList<MessageDto> messages, IReadOnlyList<TicketEventDto> events, TicketLookups lookups)
    {
        var messageIds = messages.Select(m => m.Id).ToHashSet();
        var ranked = new List<(int Rank, TimelineEntryViewModel Entry)>(messages.Count + events.Count);

        foreach (var message in messages)
        {
            ranked.Add((MessageRank, FromMessage(message)));
        }

        foreach (var ticketEvent in events)
        {
            if (ticketEvent.Type == TicketEventTypes.MessageAdded && ReadGuid(ticketEvent.PayloadJson, MessageIdKey) is { } id && messageIds.Contains(id))
            {
                continue;
            }

            ranked.Add((ticketEvent.Type == TicketEventTypes.Created ? CreatedRank : EventRank, FromEvent(ticketEvent, lookups)));
        }

        // A stable sort: entries at the same instant keep the API's order (Created, then the message, then the changes made with it).
        return ranked.Select((item, index) => (item.Rank, item.Entry, Index: index))
            .OrderBy(item => item.Entry.At).ThenBy(item => item.Rank).ThenBy(item => item.Index)
            .Select(item => item.Entry)
            .ToList();
    }

    private static TimelineEntryViewModel FromMessage(MessageDto message)
    {
        var kind = message.AuthorType == MessageAuthorTypes.Requester
            ? EntryKind.Customer
            : message.Visibility == MessageVisibilities.Internal ? EntryKind.InternalNote : EntryKind.PublicReply;
        var author = message.AuthorName ?? message.AuthorType;
        var view = new MessageViewModel(message.Id, kind, author, message.CreatedAt, message.BodyHtml, message.Attachments, message.LinkedArticles);
        return new TimelineEntryViewModel(message.Id, TimelineEntryKind.Message, message.CreatedAt, author, string.Empty, view);
    }

    private static TimelineEntryViewModel FromEvent(TicketEventDto e, TicketLookups lookups) =>
        new(e.Id, TimelineEntryKind.Event, e.OccurredAt, e.ActorName ?? e.ActorType, Describe(e, lookups), null);

    private static string Describe(TicketEventDto e, TicketLookups lookups)
    {
        using var payload = TryParse(e.PayloadJson);
        var root = payload?.RootElement;
        return e.Type switch
        {
            TicketEventTypes.Created => $"Ticket opened via {Text(root, ChannelKey) ?? "an unknown channel"}",
            TicketEventTypes.MessageAdded => "A message was added",
            TicketEventTypes.StatusChanged => $"Status changed from {Text(root, FromKey) ?? "?"} to {Text(root, ToKey) ?? "?"}",
            TicketEventTypes.Assigned => DescribeAssignment(root, lookups),
            TicketEventTypes.ProductChanged => $"Product changed from {lookups.ProductName(Id(root, FromKey))} to {lookups.ProductName(Id(root, ToKey))}",
            TicketEventTypes.PriorityChanged => $"Priority changed from {Text(root, FromKey) ?? "?"} to {Text(root, ToKey) ?? "?"}",
            TicketEventTypes.TagAdded => $"Tag {lookups.TagName(Id(root, TagIdKey))} added",
            TicketEventTypes.TagRemoved => Text(root, ReasonKey) == TagDeletedReason
                ? $"Tag {lookups.TagName(Id(root, TagIdKey))} removed because the tag was deleted"
                : $"Tag {lookups.TagName(Id(root, TagIdKey))} removed",
            TicketEventTypes.MarkedSpam => Flag(root, IsSpamKey) == false ? "Restored from spam" : "Marked as spam",
            TicketEventTypes.FollowUpCreated => "The customer replied after the ticket closed, so a follow-up ticket was opened",
            _ => $"Event: {e.Type}",
        };
    }

    private static string DescribeAssignment(JsonElement? root, TicketLookups lookups)
    {
        var from = Id(root, FromKey);
        var to = Id(root, ToKey);
        return (from, to) switch
        {
            (null, null) => "Assignment changed",
            (null, { } target) => $"Assigned to {lookups.AgentName(target)}",
            ({ } source, null) => $"Unassigned (was {lookups.AgentName(source)})",
            ({ } source, { } target) => $"Reassigned from {lookups.AgentName(source)} to {lookups.AgentName(target)}",
        };
    }

    private static JsonDocument? TryParse(string json)
    {
        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Guid? ReadGuid(string json, string key)
    {
        using var payload = TryParse(json);
        return payload is null ? null : Id(payload.RootElement, key);
    }

    private static string? Text(JsonElement? root, string key) =>
        root is { } element && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Guid? Id(JsonElement? root, string key) =>
        Guid.TryParse(Text(root, key), CultureInfo.InvariantCulture, out var id) ? id : null;

    private static bool? Flag(JsonElement? root, string key) =>
        root is { } element && element.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}

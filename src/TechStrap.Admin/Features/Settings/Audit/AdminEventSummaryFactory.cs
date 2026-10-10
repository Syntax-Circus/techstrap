using System.Globalization;
using System.Text;
using System.Text.Json;
using TechStrap.Admin.Features.Settings;
using TechStrap.Contracts.AdminEvents;

namespace TechStrap.Admin.Features.Settings.Audit;

/// <summary>
/// Turns an admin event into one plain sentence for the audit log. It reads only the ids, slugs, prefixes, kinds and counts the API puts in a payload (a payload never carries names, emails or secrets),
/// shortens every value it prints, and never returns the payload itself: no raw JSON is ever rendered, and what it returns is encoded by Razor like any other text. An unknown event type, a payload that is
/// not JSON, or a field of the wrong kind never throws: the sentence just says less.
/// </summary>
public static class AdminEventSummaryFactory
{
    public static string Summarize(string? type, string? payload)
    {
        try
        {
            return SummarizeCore(type ?? string.Empty, payload ?? string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            // One bad row never takes the page down: the sentence just says less.
            return Humanize(type);
        }
    }

    private static string SummarizeCore(string type, string payload)
    {
        using var document = TryParse(payload);
        var root = document?.RootElement is { ValueKind: JsonValueKind.Object } obj ? obj : (JsonElement?)null;

        return type switch
        {
            AdminEventTypes.ProductCreated => Join("Created product", Text(root, "productKey"), Wrap("ticket prefix", Text(root, "numberPrefix")), "Created a product"),
            AdminEventTypes.ProductUpdated => Changed("Updated a product", root, ProductChange),
            AdminEventTypes.ApiKeyCreated => ApiKeyCreated(root),
            AdminEventTypes.ApiKeyRevoked => Join("Revoked API key", Text(root, "keyPrefix"), null, "Revoked an API key"),
            AdminEventTypes.AgentUpdated => Flag(root, "isActive") switch
            {
                true => "Activated an agent",
                false => "Deactivated an agent",
                _ => "Changed an agent",
            },
            AdminEventTypes.TagCreated => Join("Created tag", Text(root, "slug"), null, "Created a tag"),
            AdminEventTypes.TagUpdated => Changed(Join("Updated tag", Text(root, "slug"), null, "Updated a tag"), root, TagChange),
            AdminEventTypes.TagDeleted => TagDeleted(root),
            AdminEventTypes.RequesterErased => RequesterErased(root),
            AdminEventTypes.TicketDeleted => TicketDeleted(root),
            AdminEventTypes.DeadLetterRetried => DeadLetter(root, "Retried a failed", "after"),
            AdminEventTypes.DeadLetterDiscarded => DeadLetter(root, "Discarded a failed", "after"),
            AdminEventTypes.SiteSettingsUpdated => Join("Changed the default theme pack to", Text(root, "defaultPack"), null, "Changed the default theme pack"),
            _ => Humanize(type),
        };
    }

    /// <summary>The words for what an event is about. A subject type this app does not know is shown as it came, shortened.</summary>
    public static string SubjectLabel(string? subjectType) => subjectType switch
    {
        AdminSubjectTypes.Product => "Product",
        AdminSubjectTypes.ApiKey => "API key",
        AdminSubjectTypes.Agent => "Agent",
        AdminSubjectTypes.Tag => "Tag",
        AdminSubjectTypes.Requester => "Requester",
        AdminSubjectTypes.Ticket => "Ticket",
        AdminSubjectTypes.EmailOutbox => "Email",
        AdminSubjectTypes.SiteSettings => "Site settings",
        _ => Clip(subjectType),
    };

    // ---- the sentences that need more than a name ----------------------------------------------------------------

    private static string ApiKeyCreated(JsonElement? root)
    {
        var kind = Text(root, "kind");
        var prefix = Text(root, "keyPrefix");
        var head = kind is null ? "Created an API key" : $"Created a {kind} API key";
        return prefix is null ? head : $"{head} ({prefix})";
    }

    private static string TagDeleted(JsonElement? root)
    {
        var head = Join("Deleted tag", Text(root, "slug"), null, "Deleted a tag");
        return Count(root, "detachedTicketCount") is { } count ? $"{head}, removed from {Plural(count, "ticket", "tickets")}" : head;
    }

    private static string RequesterErased(JsonElement? root)
    {
        var parts = new List<string>();
        Add(parts, Count(root, "tickets"), "ticket", "tickets");
        Add(parts, Count(root, "messages"), "message", "messages");
        Add(parts, Count(root, "attachments"), "attachment", "attachments");
        Add(parts, Count(root, "links"), "access link", "access links");
        Add(parts, Count(root, "outboxRows"), "queued email", "queued emails");
        return parts.Count == 0 ? "Erased a requester" : $"Erased a requester: {string.Join(", ", parts)}";
    }

    private static string TicketDeleted(JsonElement? root)
    {
        var head = Join("Deleted ticket", Text(root, "number"), null, "Deleted a ticket");
        var parts = new List<string>();
        Add(parts, Count(root, "messageCount"), "message", "messages");
        Add(parts, Count(root, "attachmentCount"), "attachment", "attachments");
        return parts.Count == 0 ? head : $"{head} ({string.Join(", ", parts)})";
    }

    private static string DeadLetter(JsonElement? root, string verb, string attemptsWord)
    {
        var kind = Text(root, "kind");
        var head = kind is null ? $"{verb} email" : $"{verb} {EmailKinds.Label(kind).ToLowerInvariant()} email";
        return Count(root, "attempts") is { } attempts ? $"{head} {attemptsWord} {Plural(attempts, "attempt", "attempts")}" : head;
    }

    private static string Changed(string head, JsonElement? root, Func<string, string> label)
    {
        var changed = List(root, "changed");
        return changed.Count == 0 ? head : $"{head}: {string.Join(", ", changed.Select(label))}";
    }

    private static string ProductChange(string field) => field switch
    {
        "name" => "name",
        "branding" => "branding",
        "isActive" => "active status",
        "skin" => "skin",
        _ => field,
    };

    private static string TagChange(string field) => field switch
    {
        "name" => "name",
        "colour" => "color",
        _ => field,
    };

    // ---- reading a payload that may be anything ------------------------------------------------------------------

    private static JsonDocument? TryParse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement? root, string name) =>
        root is { } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && StringOf(value) is { Length: > 0 } text ? Clip(text) : null;

    // A JSON string with a lone surrogate escape parses but cannot be read as text: it counts as missing.
    private static string? StringOf(JsonElement value)
    {
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static int? Count(JsonElement? root, string name) =>
        root is { } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0 ? number : null;

    private static bool? Flag(JsonElement? root, string name) =>
        root is { } element && element.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) ? value.GetBoolean() : null;

    private static List<string> List(JsonElement? root, string name)
    {
        var result = new List<string>();
        if (root is { } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            result.AddRange(value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => Clip(StringOf(item))).Where(text => text.Length > 0).Take(8));
        }

        return result;
    }

    // ---- wording --------------------------------------------------------------------------------------------------

    // "Created product orbitly (ticket prefix ORB)": the head, then the first value, then an optional second; with no values, the fallback sentence.
    private static string Join(string head, string? first, string? second, string fallback)
    {
        if (first is null && second is null)
        {
            return fallback;
        }

        return string.Join(' ', new[] { head, first, second }.Where(part => !string.IsNullOrEmpty(part)));
    }

    private static string? Wrap(string? label, string? value) =>
        value is null ? null : label is null ? $"({value})" : $"({label} {value})";

    private static void Add(List<string> parts, int? count, string singular, string plural)
    {
        if (count is { } value)
        {
            parts.Add(Plural(value, singular, plural));
        }
    }

    private static string Plural(int count, string singular, string plural) => $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural)}";

    /// <summary>"DeadLetterRetried" gives "Dead letter retried": what an event type this app does not know is shown as.</summary>
    private static string Humanize(string? type)
    {
        var text = Clip(type);
        var builder = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (i > 0 && char.IsUpper(text[i]) && !char.IsUpper(text[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(i == 0 || !char.IsUpper(text[i]) ? text[i] : char.ToLowerInvariant(text[i]));
        }

        return builder.Length == 0 ? "Admin event" : builder.ToString();
    }

    private static string Clip(string? value) => SafeText.Clip(value);
}

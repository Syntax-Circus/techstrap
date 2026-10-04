using TechStrap.Contracts.AdminEvents;

namespace TechStrap.Admin.Features.Settings.Audit;

/// <summary>What the audit log can be filtered by (the API supports only the subject type and the actor, plus the as-of time that keeps paging stable), and the link for a filter set.</summary>
public static class AuditFilters
{
    public static IReadOnlyList<string> Subjects { get; } =
    [
        AdminSubjectTypes.Product,
        AdminSubjectTypes.ApiKey,
        AdminSubjectTypes.Agent,
        AdminSubjectTypes.Tag,
        AdminSubjectTypes.Requester,
        AdminSubjectTypes.Ticket,
        AdminSubjectTypes.EmailOutbox,
    ];

    /// <summary>The canonical spelling of a subject type from the query string (case-insensitive), or null when it is blank or unknown: an unknown value is dropped, never sent.</summary>
    public static string? CanonicalSubject(string? value) => Subjects.FirstOrDefault(s => s.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Uri(string? subject, Guid? actor, int page)
    {
        var query = new List<string>();
        if (subject is not null)
        {
            query.Add($"{AuditCopy.SubjectKey}={System.Uri.EscapeDataString(subject)}");
        }

        if (actor is { } id)
        {
            query.Add($"{AuditCopy.ActorKey}={id}");
        }

        if (page > 1)
        {
            query.Add($"{AuditCopy.PageKey}={page}");
        }

        return query.Count == 0 ? "/settings/audit" : $"/settings/audit?{string.Join('&', query)}";
    }
}

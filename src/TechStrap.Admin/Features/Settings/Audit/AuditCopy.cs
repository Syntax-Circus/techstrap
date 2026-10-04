namespace TechStrap.Admin.Features.Settings.Audit;

/// <summary>The copy of the audit log: who changed configuration and who ran privacy and operations actions. Read-only.</summary>
public static class AuditCopy
{
    public const int PageSize = 25;
    public const string SubjectKey = "subject";
    public const string ActorKey = "actor";
    public const string PageKey = "page";

    public const string Heading = "Audit log";
    public const string Intro = "Changes to products, API keys, agents and tags, and who ran an erase, a delete or a failed-email action. Newest first.";
    public const string Loading = "Loading the audit log";
    public const string LoadFailed = "Couldn't load the audit log.";
    public const string NoEvents = "No audit events yet";
    public const string NoMatches = "No events match these filters";
    public const string ClearFilters = "Clear filters";
    public const string Refresh = "Refresh";

    public const string SubjectFilter = "What changed";
    public const string ActorFilter = "Who";
    public const string AllSubjects = "Everything";
    public const string AllActors = "Anyone";
    public const string ColumnWhen = "When";
    public const string ColumnWho = "Who";
    public const string ColumnWhat = "What changed";
    public const string ColumnSummary = "What happened";
    public const string UnknownActor = "Unknown agent";
}

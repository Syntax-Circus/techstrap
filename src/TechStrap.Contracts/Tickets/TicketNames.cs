namespace TechStrap.Contracts.Tickets;

/// <summary>Wire names for ticket status enum. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class TicketStatuses
{
    public const string New = "New";
    public const string Open = "Open";
    public const string Pending = "Pending";
    public const string Solved = "Solved";
    public const string Closed = "Closed";
}

/// <summary>Wire names for ticket priority enum. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class TicketPriorities
{
    public const string Low = "Low";
    public const string Normal = "Normal";
    public const string High = "High";
    public const string Urgent = "Urgent";
}

/// <summary>Queue views (D-024). Spam lists only spam; every other view excludes it.</summary>
public static class TicketViews
{
    public const string Unassigned = "Unassigned";
    public const string Mine = "Mine";
    public const string Open = "Open";
    public const string Pending = "Pending";
    public const string All = "All";
    public const string Spam = "Spam";
    public const string Default = All;
}

/// <summary>Wire names for ticket event type enum. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class TicketEventTypes
{
    public const string Created = "Created";
    public const string MessageAdded = "MessageAdded";
    public const string StatusChanged = "StatusChanged";
    public const string Assigned = "Assigned";
    public const string ProductChanged = "ProductChanged";
    public const string PriorityChanged = "PriorityChanged";
    public const string TagAdded = "TagAdded";
    public const string TagRemoved = "TagRemoved";
    public const string MarkedSpam = "MarkedSpam";
    public const string FollowUpCreated = "FollowUpCreated";
}

/// <summary>Wire names for message author type enum. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class MessageAuthorTypes
{
    public const string Requester = "Requester";
    public const string Agent = "Agent";
    public const string System = "System";
}

/// <summary>Wire names for message visibility enum. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class MessageVisibilities
{
    public const string Public = "Public";
    public const string Internal = "Internal";
}

/// <summary>Limits on agent ticket operations.</summary>
public static class TicketOperationLimits
{
    public const int MaxLinkedArticles = 10;
}

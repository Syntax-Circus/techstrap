namespace TechStrap.Contracts.Tickets;

/// <summary>Wire names for ticket status enum. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class TicketStatuses
{
    /// <summary>Just arrived; no agent has acted on it yet.</summary>
    public const string New = "New";
    /// <summary>Being worked on by the support team.</summary>
    public const string Open = "Open";
    /// <summary>Waiting for a reply from the requester.</summary>
    public const string Pending = "Pending";
    /// <summary>Answered and awaiting confirmation that the problem is gone.</summary>
    public const string Solved = "Solved";
    /// <summary>Finished. A requester reply to a closed ticket opens a follow-up ticket.</summary>
    public const string Closed = "Closed";
}

/// <summary>Wire names for ticket priority enum. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class TicketPriorities
{
    /// <summary>Can wait.</summary>
    public const string Low = "Low";
    /// <summary>The default priority.</summary>
    public const string Normal = "Normal";
    /// <summary>Should be answered ahead of normal tickets.</summary>
    public const string High = "High";
    /// <summary>Needs attention first.</summary>
    public const string Urgent = "Urgent";
}

/// <summary>Queue views (D-024). Spam lists only spam; every other view excludes it.</summary>
public static class TicketViews
{
    /// <summary>Tickets with no assigned agent that are New, Open or Pending (not spam).</summary>
    public const string Unassigned = "Unassigned";
    /// <summary>Tickets assigned to the signed-in agent that are New, Open or Pending (not spam).</summary>
    public const string Mine = "Mine";
    /// <summary>Tickets that are New or Open (not spam).</summary>
    public const string Open = "Open";
    /// <summary>Tickets in the Pending status.</summary>
    public const string Pending = "Pending";
    /// <summary>Every ticket except spam.</summary>
    public const string All = "All";
    /// <summary>Only tickets marked as spam.</summary>
    public const string Spam = "Spam";
    /// <summary>The view used when none is given: <see cref="All"/>.</summary>
    public const string Default = All;
}

/// <summary>Wire names for ticket event type enum. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class TicketEventTypes
{
    /// <summary>The ticket was created.</summary>
    public const string Created = "Created";
    /// <summary>A message was added by the requester, an agent or the system.</summary>
    public const string MessageAdded = "MessageAdded";
    /// <summary>The status changed.</summary>
    public const string StatusChanged = "StatusChanged";
    /// <summary>The ticket was assigned to an agent or unassigned.</summary>
    public const string Assigned = "Assigned";
    /// <summary>The ticket was moved to another product.</summary>
    public const string ProductChanged = "ProductChanged";
    /// <summary>The priority changed.</summary>
    public const string PriorityChanged = "PriorityChanged";
    /// <summary>A tag was added.</summary>
    public const string TagAdded = "TagAdded";
    /// <summary>A tag was removed.</summary>
    public const string TagRemoved = "TagRemoved";
    /// <summary>The ticket was marked as spam.</summary>
    public const string MarkedSpam = "MarkedSpam";
    /// <summary>A follow-up ticket was created from this closed ticket.</summary>
    public const string FollowUpCreated = "FollowUpCreated";
}

/// <summary>Wire names for message author type enum. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class MessageAuthorTypes
{
    /// <summary>The customer who raised the ticket.</summary>
    public const string Requester = "Requester";
    /// <summary>A member of the support team.</summary>
    public const string Agent = "Agent";
    /// <summary>TechStrap itself, for automatic messages.</summary>
    public const string System = "System";
}

/// <summary>Wire names for message visibility enum. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class MessageVisibilities
{
    /// <summary>Visible to the requester.</summary>
    public const string Public = "Public";
    /// <summary>An internal note, visible to agents only.</summary>
    public const string Internal = "Internal";
}

/// <summary>Limits on agent ticket operations.</summary>
public static class TicketOperationLimits
{
    /// <summary>The most knowledge base articles one public agent reply may link (10). The API refuses more.</summary>
    public const int MaxLinkedArticles = 10;
}

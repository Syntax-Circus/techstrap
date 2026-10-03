namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// What a status stamp says. This is a presentation enum owned by the Admin UI (Admin may not reference Domain). It is the five
/// ticket statuses plus Spam, which is a flag on a ticket in the domain but gets its own double-border stamp (docs/BRAND.md section 18).
/// </summary>
public enum StampStatus
{
    New,
    Open,
    Pending,
    Solved,
    Closed,
    Spam,
}

/// <summary>Where the stamp is shown: straight and single-bordered in lists, tilted on the ticket view.</summary>
public enum StampVariant
{
    Queue,
    Ticket,
}

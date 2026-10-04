namespace TechStrap.Contracts.Tickets;

/// <summary>
/// Result of a customer reply. For a reply on an open ticket: TicketNumber is that ticket, MessageId the new message, and
/// FollowUpViewUrl null. For a reply on a Closed ticket: TicketNumber is the follow-up's number, MessageId its first message,
/// and FollowUpViewUrl the follow-up's link (a fresh token; the only response that carries a link, D-038).
/// </summary>
public sealed record CustomerReplyResponse(string TicketNumber, Guid MessageId, bool FollowUpCreated, string? FollowUpViewUrl);

public sealed record AddCustomerReplyRequest(string? Body);

public sealed record RequestNewAccessLinkRequest(string? Email);

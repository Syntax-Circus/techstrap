namespace TechStrap.Contracts.Tickets;

/// <summary>
/// Result of a customer reply. For a reply on an open ticket: TicketNumber is that ticket, MessageId the new message, and
/// FollowUpViewUrl null. For a reply on a Closed ticket: TicketNumber is the follow-up's number, MessageId its first message,
/// and FollowUpViewUrl the follow-up's link (a fresh token; the only response that carries a link, D-038).
/// </summary>
/// <param name="TicketNumber">The number of the ticket the reply landed on.</param>
/// <param name="MessageId">The id of the new message.</param>
/// <param name="FollowUpCreated">True when the reply was on a Closed ticket and started a follow-up.</param>
/// <param name="FollowUpViewUrl">The follow-up's link, or null when no follow-up was created.</param>
public sealed record CustomerReplyResponse(string TicketNumber, Guid MessageId, bool FollowUpCreated, string? FollowUpViewUrl);

/// <summary>A requester's reply to their ticket.</summary>
/// <param name="Body">The reply text; required.</param>
public sealed record AddCustomerReplyRequest(string? Body);

/// <summary>Asks for a fresh access link to be emailed to the requester.</summary>
/// <param name="Email">The email address the ticket was raised from; required.</param>
public sealed record RequestNewAccessLinkRequest(string? Email);

namespace TechStrap.Portal.Tickets;

/// <summary>A file attached to a public message: the name the API stored (plain text, encoded by the page), a readable size and the Portal's own link to it (the pass-through, never the API's).</summary>
public sealed record CustomerAttachmentViewModel(Guid Id, string FileName, string SizeText, string Href);

/// <summary>
/// One message of the public conversation. <see cref="Author"/> is a plain-text label: "You" for the customer's own, the name the API resolved for an agent (shown as it came: no email, no id, no avatar) and a
/// neutral word for a system message. <see cref="BodyHtml"/> is the API's sanitised HTML and the only thing on the page that is not encoded; <c>CustomerMessageBody</c> is the single place it is rendered.
/// </summary>
public sealed record CustomerMessageViewModel(Guid Id, string Author, bool IsCustomer, string BodyHtml, DateTimeOffset CreatedAt, IReadOnlyList<CustomerAttachmentViewModel> Attachments);

/// <summary>What the ticket page needs and nothing more: no internal id, no agent detail, no product beyond its key (which only selects the theme).</summary>
public sealed record CustomerTicketViewModel(
    string Number,
    string ProductKey,
    string Subject,
    string StatusLabel,
    string? StatusNote,
    bool IsSolved,
    bool IsClosed,
    IReadOnlyList<CustomerMessageViewModel> Messages);

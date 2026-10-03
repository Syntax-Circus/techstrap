namespace TechStrap.Contracts.Intake;

/// <summary>A new ticket from a customer. Email, subject and body are required. <c>ExternalUserRef</c> and <c>Metadata</c> are trusted only from trusted API keys (D-001, D-032). Attachments arrive separately as multipart files on the web form.</summary>
public sealed record SubmitTicketRequest(string? Email, string? Name, string? Subject, string? Body, string? ExternalUserRef, IReadOnlyDictionary<string, string>? Metadata);

/// <summary><c>ViewUrl</c> is set for API-key callers only; web-form submitters get the link by email. <c>Warnings</c> holds codes from <see cref="IntakeWarnings"/>.</summary>
public sealed record SubmitTicketResponse(string TicketNumber, string? ViewUrl, IReadOnlyList<string> Warnings);

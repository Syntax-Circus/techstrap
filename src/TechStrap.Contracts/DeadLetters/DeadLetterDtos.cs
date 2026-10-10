namespace TechStrap.Contracts.DeadLetters;

/// <summary>
/// A dead-lettered email as an admin sees it. Recipient is masked (first character of the local part, then ***@ and the domain); the
/// payload, which holds a portal link, is never exposed (D-039). LastError is a sanitized category code, never server text.
/// </summary>
public sealed record DeadLetterDto(
    Guid Id, string Kind, string Recipient, Guid? TicketId, Guid? ProductId, int Attempts, string? LastError, DateTimeOffset CreatedAt);

namespace TechStrap.Client.Maui;

/// <summary>What the app's support form collects for one ticket.</summary>
/// <param name="Subject">The ticket subject.</param>
/// <param name="Message">The ticket body.</param>
/// <param name="RequesterEmail">The requester's email address.</param>
/// <param name="RequesterName">The requester's name, or <see langword="null"/>.</param>
/// <param name="Metadata">
/// Extra key/value pairs from the app. A key that is one of <see cref="TechStrap.Contracts.Intake.TicketMetadataKeys"/> is ignored (the device context is the helper's to set);
/// a blank value is dropped and a long value is cut to the server's limit. A blank or over-long key, too many keys, or too much in all fails before anything is sent.
/// </param>
/// <param name="IdempotencyKey">A stable key for this ticket, or <see langword="null"/> to send without one. Supply the same key on every retry of the same ticket.</param>
public sealed record MauiTicketDraft(
    string Subject,
    string Message,
    string RequesterEmail,
    string? RequesterName = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? IdempotencyKey = null);

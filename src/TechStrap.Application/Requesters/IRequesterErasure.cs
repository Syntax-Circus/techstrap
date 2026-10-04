namespace TechStrap.Application.Requesters;

public sealed record RequesterErasureResult(int Tickets, int Messages, int Attachments, int Tokens, int OutboxRows, IReadOnlyList<string> StorageKeys);

/// <summary>
/// The bulk half of erasing a requester (D-039). It runs in the caller's unit of work, never commits, and is the only writer that changes
/// <c>Message.Body</c> after a message is created: the Domain has no way to change a body, on purpose, so this port updates the rows directly.
/// The caller deletes the returned storage keys through the attachment store after its own commit.
/// </summary>
public interface IRequesterErasure
{
    Task<RequesterErasureResult> EraseDataAsync(Guid requesterId, string email, DateTimeOffset now, CancellationToken cancellationToken);
}

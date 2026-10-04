using TechStrap.Contracts.DeadLetters;

namespace TechStrap.Admin.Features.Ops.DeadLetters;

/// <summary>One failed email. The recipient arrives already masked by the API ("a***@example.com"); the page never has the full address.</summary>
internal sealed record DeadLetterRowViewModel(Guid Id, string Kind, string Recipient, Guid? TicketId, int Attempts, string LastError, DateTimeOffset CreatedAt)
{
    public static DeadLetterRowViewModel From(DeadLetterDto letter) =>
        new(letter.Id, letter.Kind, letter.Recipient, letter.TicketId, letter.Attempts, DeadLettersCopy.ErrorLabel(letter.LastError), letter.CreatedAt);
}

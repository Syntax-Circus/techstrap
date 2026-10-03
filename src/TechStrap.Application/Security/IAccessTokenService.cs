using TechStrap.Domain;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Security;

/// <summary>Customer ticket links (D-032). The plaintext token is returned once and only its hash is stored.</summary>
public interface IAccessTokenService
{
    /// <summary>A new 256-bit token for the ticket and requester, ready to stage with ITicketRepository.AddAccessToken.</summary>
    DomainResult<IssuedAccessToken> Issue(Guid ticketId, Guid requesterId);

    /// <summary>Hash used for storage and lookup: "sha256:" + lower-case hex of the UTF-8 token.</summary>
    string Hash(string token);
}

public sealed record IssuedAccessToken(string PlaintextToken, TicketAccessToken Token);

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using TechStrap.Application.Security;
using TechStrap.Domain;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.Security;

internal sealed class AccessTokenService(TimeProvider clock) : IAccessTokenService
{
    private const int TokenBytes = 32;
    private const string HashScheme = "sha256:";

    public DomainResult<IssuedAccessToken> Issue(Guid ticketId, Guid requesterId)
    {
        var plaintext = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var token = TicketAccessToken.Issue(ticketId, requesterId, Hash(plaintext), clock);
        return token.IsFailure ? token.Error! : DomainResult<IssuedAccessToken>.Ok(new IssuedAccessToken(plaintext, token.Value));
    }

    public string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return HashScheme + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}

using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Security;

namespace TechStrap.Application.Tickets.Customer;

internal static class CustomerAccess
{
    /// <summary>Hard cap on the header value before hashing (a real token is 43 characters).</summary>
    public const int MaxTokenLength = 128;

    /// <summary>
    /// Resolves a raw X-Ticket-Token value to its ticket and requester, or the uniform NotFound. Always performs the hash lookup
    /// for a non-empty value of sane length so a well-formed guess and a real token cost the same work. Loads tracked entities
    /// (callers may RecordUse/Update in their unit of work).
    /// </summary>
    public static async Task<Result<CustomerContext>> ResolveAsync(
        string? rawToken, IAccessTokenService tokens, ITicketRepository tickets, IRequesterRepository requesters,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > MaxTokenLength)
        {
            return NotFound();
        }

        var token = await tickets.GetAccessTokenByHashAsync(tokens.Hash(rawToken.Trim()), cancellationToken);
        if (token is null || !token.IsValid(clock))
        {
            return NotFound();
        }

        var ticket = await tickets.GetByIdAsync(token.TicketId, cancellationToken);
        if (ticket is null)
        {
            return NotFound();
        }

        var requester = await requesters.GetByIdAsync(token.RequesterId, cancellationToken);
        return requester is null || requester.IsErased
            ? NotFound()
            : Result<CustomerContext>.Success(new CustomerContext(token, ticket, requester));
    }

    private static Result<CustomerContext> NotFound() => Result<CustomerContext>.Failure(CustomerErrors.NotFound());
}

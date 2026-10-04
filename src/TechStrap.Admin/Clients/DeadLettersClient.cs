using SyntaxCircus.Common;
using TechStrap.Contracts.DeadLetters;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Clients;

/// <summary>Emails that ran out of attempts (Admin only: every call is a 403 for a plain agent, so only an admin session may make one).</summary>
public interface IDeadLettersClient
{
    /// <summary><c>GET /api/dead-letters</c>: the recipient is masked and the payload is never returned.</summary>
    Task<Result<PagedResponse<DeadLetterDto>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/dead-letters/{id}/retry</c> (204): puts the email back in the queue. 404 outbox-not-found; 409 outbox-not-dead-lettered (someone else already
    /// retried or discarded it). Never retried by the client.
    /// </summary>
    Task<Result> RetryAsync(Guid id, CancellationToken cancellationToken);

    /// <summary><c>DELETE /api/dead-letters/{id}</c> (204): the email is dropped and will never be sent. Same 404 and 409 as retry. Never retried by the client.</summary>
    Task<Result> DiscardAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>How many emails are dead-lettered: the <c>TotalCount</c> of a page of one, for the navigation badge.</summary>
    Task<Result<int>> CountAsync(CancellationToken cancellationToken);
}

internal sealed class DeadLettersClient(ApiConnection connection) : IDeadLettersClient
{
    public Task<Result<PagedResponse<DeadLetterDto>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        connection.GetAsync<PagedResponse<DeadLetterDto>>(ApiUri.Build("api/dead-letters", ("page", page), ("pageSize", pageSize)), cancellationToken);

    public Task<Result> RetryAsync(Guid id, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Post, $"api/dead-letters/{id}/retry", null, cancellationToken);

    public Task<Result> DiscardAsync(Guid id, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Delete, $"api/dead-letters/{id}", null, cancellationToken);

    public async Task<Result<int>> CountAsync(CancellationToken cancellationToken)
    {
        var page = await ListAsync(1, 1, cancellationToken);
        return page.IsSuccess ? Result<int>.Success(page.Value.TotalCount) : Result<int>.Failure(page.Errors[0], [.. page.Errors.Skip(1)]);
    }
}

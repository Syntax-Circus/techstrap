using System.Net.Http.Headers;
using SyntaxCircus.Common;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Clients;

/// <summary>
/// A file to send with a reply. The stream is opened when the request is built and closed when it has been sent, so the same
/// <see cref="ReplyAttachment"/> can be sent again after a failure (the composer keeps the browser files until a send succeeds).
/// </summary>
public sealed record ReplyAttachment(string FileName, string ContentType, Func<Stream> OpenRead);

/// <summary>
/// Every ticket operation of the API (D-036). Every write sends the <c>RowVersion</c> in its request (or query) and returns the new <see cref="TicketStateDto"/>;
/// a stale version is a Conflict result with code <see cref="ApiErrorCodes.ConcurrencyConflict"/>. Mutations are never retried.
/// </summary>
public interface ITicketsClient
{
    /// <summary><c>GET /api/tickets</c>: the request is sent as the query string; Page and PageSize are always sent.</summary>
    Task<Result<PagedResponse<TicketSummaryDto>>> ListAsync(ListTicketsRequest request, CancellationToken cancellationToken);

    /// <summary><c>GET /api/tickets/counts</c>.</summary>
    Task<Result<TicketViewCountsResponse>> GetCountsAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/tickets/{reference}</c>: <paramref name="reference"/> is the ticket id or its number (for example ORB-42). 404 is code ticket-not-found.</summary>
    Task<Result<TicketDetailDto>> GetAsync(string reference, CancellationToken cancellationToken);

    /// <summary><c>POST /api/tickets/{id}/replies</c> as multipart/form-data (a public reply, emailed to the customer).</summary>
    Task<Result<AgentMessageResponse>> ReplyAsync(Guid ticketId, AddAgentReplyRequest request, IReadOnlyList<ReplyAttachment> attachments, CancellationToken cancellationToken);

    /// <summary><c>POST /api/tickets/{id}/notes</c> (an internal note).</summary>
    Task<Result<AgentMessageResponse>> AddNoteAsync(Guid ticketId, AddInternalNoteRequest request, CancellationToken cancellationToken);

    Task<Result<TicketStateDto>> ChangeStatusAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken);

    Task<Result<TicketStateDto>> AssignAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken);

    Task<Result<TicketStateDto>> ChangePriorityAsync(Guid ticketId, ChangeTicketPriorityRequest request, CancellationToken cancellationToken);

    Task<Result<TicketStateDto>> MoveProductAsync(Guid ticketId, MoveTicketProductRequest request, CancellationToken cancellationToken);

    /// <summary>Idempotent: adding a tag the ticket already has succeeds.</summary>
    Task<Result<TicketStateDto>> AddTagAsync(Guid ticketId, AddTicketTagRequest request, CancellationToken cancellationToken);

    /// <summary><c>DELETE /api/tickets/{id}/tags/{tagId}?rowVersion=N</c>: the row version travels in the query string and is required.</summary>
    Task<Result<TicketStateDto>> RemoveTagAsync(Guid ticketId, Guid tagId, uint rowVersion, CancellationToken cancellationToken);

    /// <summary><c>PUT /api/tickets/{id}/spam</c>: IsSpam true marks, false restores (Not spam).</summary>
    Task<Result<TicketStateDto>> SetSpamAsync(Guid ticketId, MarkTicketSpamRequest request, CancellationToken cancellationToken);

    /// <summary><c>DELETE /api/tickets/{id}</c> (Admin): hard delete, no row version. 204.</summary>
    Task<Result> DeleteAsync(Guid ticketId, CancellationToken cancellationToken);
}

/// <summary>Erasing a requester (Admin).</summary>
public interface IRequestersClient
{
    /// <summary><c>POST /api/requesters/{id}/erase</c>: <paramref name="requesterId"/> is <c>TicketDetailDto.Requester.Id</c>. Idempotent, 204.</summary>
    Task<Result> EraseAsync(Guid requesterId, CancellationToken cancellationToken);
}

internal sealed class TicketsClient(ApiConnection connection) : ITicketsClient
{
    private static string Ticket(Guid id) => $"api/tickets/{id}";

    public Task<Result<PagedResponse<TicketSummaryDto>>> ListAsync(ListTicketsRequest request, CancellationToken cancellationToken) =>
        connection.GetAsync<PagedResponse<TicketSummaryDto>>(
            ApiUri.Build(
                "api/tickets",
                ("view", request.View),
                ("productId", request.ProductId),
                ("status", request.Status),
                ("priority", request.Priority),
                ("assigneeId", request.AssigneeId),
                ("tagId", request.TagId),
                ("requesterId", request.RequesterId),
                ("search", request.Search),
                ("page", request.Page),
                ("pageSize", request.PageSize)),
            cancellationToken);

    public Task<Result<TicketViewCountsResponse>> GetCountsAsync(CancellationToken cancellationToken) =>
        connection.GetAsync<TicketViewCountsResponse>("api/tickets/counts", cancellationToken);

    public Task<Result<TicketDetailDto>> GetAsync(string reference, CancellationToken cancellationToken) =>
        connection.GetAsync<TicketDetailDto>($"api/tickets/{Uri.EscapeDataString(reference)}", cancellationToken);

    public async Task<Result<AgentMessageResponse>> ReplyAsync(Guid ticketId, AddAgentReplyRequest request, IReadOnlyList<ReplyAttachment> attachments, CancellationToken cancellationToken)
    {
        var streams = new List<Stream>();
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(request.Body ?? string.Empty), "Body");
            foreach (var articleId in request.LinkedArticleIds ?? [])
            {
                form.Add(new StringContent(articleId.ToString()), "LinkedArticleIds");
            }

            if (!string.IsNullOrEmpty(request.StatusAfter))
            {
                form.Add(new StringContent(request.StatusAfter), "StatusAfter");
            }

            if (request.RowVersion is { } rowVersion)
            {
                form.Add(new StringContent(rowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), "RowVersion");
            }

            foreach (var attachment in attachments)
            {
                var stream = attachment.OpenRead();
                streams.Add(stream);
                var file = new StreamContent(stream);
                file.Headers.ContentType = MediaTypeHeaderValue.TryParse(attachment.ContentType, out var type) ? type : new MediaTypeHeaderValue("application/octet-stream");
                form.Add(file, "Attachments", attachment.FileName);
            }

            return await connection.SendContentAsync<AgentMessageResponse>(HttpMethod.Post, $"{Ticket(ticketId)}/replies", form, cancellationToken);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    public Task<Result<AgentMessageResponse>> AddNoteAsync(Guid ticketId, AddInternalNoteRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<AgentMessageResponse>(HttpMethod.Post, $"{Ticket(ticketId)}/notes", request, cancellationToken);

    public Task<Result<TicketStateDto>> ChangeStatusAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Put, $"{Ticket(ticketId)}/status", request, cancellationToken);

    public Task<Result<TicketStateDto>> AssignAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Put, $"{Ticket(ticketId)}/assignee", request, cancellationToken);

    public Task<Result<TicketStateDto>> ChangePriorityAsync(Guid ticketId, ChangeTicketPriorityRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Put, $"{Ticket(ticketId)}/priority", request, cancellationToken);

    public Task<Result<TicketStateDto>> MoveProductAsync(Guid ticketId, MoveTicketProductRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Put, $"{Ticket(ticketId)}/product", request, cancellationToken);

    public Task<Result<TicketStateDto>> AddTagAsync(Guid ticketId, AddTicketTagRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Post, $"{Ticket(ticketId)}/tags", request, cancellationToken);

    public Task<Result<TicketStateDto>> RemoveTagAsync(Guid ticketId, Guid tagId, uint rowVersion, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Delete, ApiUri.Build($"{Ticket(ticketId)}/tags/{tagId}", ("rowVersion", rowVersion)), null, cancellationToken);

    public Task<Result<TicketStateDto>> SetSpamAsync(Guid ticketId, MarkTicketSpamRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Put, $"{Ticket(ticketId)}/spam", request, cancellationToken);

    public Task<Result> DeleteAsync(Guid ticketId, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Delete, Ticket(ticketId), null, cancellationToken);
}

internal sealed class RequestersClient(ApiConnection connection) : IRequestersClient
{
    public Task<Result> EraseAsync(Guid requesterId, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Post, $"api/requesters/{requesterId}/erase", null, cancellationToken);
}

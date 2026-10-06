using SyntaxCircus.Common;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Portal.Clients;

internal sealed class CustomerTicketClient(ApiConnection api) : ICustomerTicketClient
{
    private const string TicketPath = "api/customer/ticket";

    public Task<Result<CustomerTicketDto>> GetAsync(TicketToken token, CancellationToken cancellationToken) =>
        api.GetAsync<CustomerTicketDto>(TicketPath, token, cancellationToken);

    public Task<Result<CustomerReplyResponse>> ReplyAsync(TicketToken token, CustomerReply reply, CancellationToken cancellationToken)
    {
        var form = MultipartForm.Build([new("Body", reply.Body)], reply.Attachments);
        return api.SendContentAsync<CustomerReplyResponse>(HttpMethod.Post, $"{TicketPath}/replies", form, token, cancellationToken);
    }

    public Task<Result> RequestAccessLinkAsync(string email, CancellationToken cancellationToken) =>
        api.SendAsync(HttpMethod.Post, "api/customer/access-link", new RequestNewAccessLinkRequest(email), cancellationToken);

    public Task<Result<ApiDownload>> OpenAttachmentAsync(TicketToken token, Guid attachmentId, CancellationToken cancellationToken) =>
        api.OpenStreamAsync($"api/customer/attachments/{attachmentId}", token, cancellationToken);
}

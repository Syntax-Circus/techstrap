using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Clients;

internal sealed class PublicTicketClient(ApiConnection api) : IPublicTicketClient
{
    public Task<Result<SubmitTicketResponse>> SubmitAsync(string productKey, NewTicketRequest request, CancellationToken cancellationToken)
    {
        if (!ProductKeyShape.IsWellFormed(productKey))
        {
            return Task.FromResult(Result<SubmitTicketResponse>.Failure(ProblemMapping.NotFound()));
        }

        // The form is built only now, so a refused key opens no file. The request disposes it, which closes the file streams.
        var form = MultipartForm.Build(
            [
                new("Email", request.Email),
                new("Name", request.Name),
                new("Subject", request.Subject),
                new("Body", request.Body),
                new("Website", request.Website),
            ],
            request.Attachments);
        return api.SendContentAsync<SubmitTicketResponse>(HttpMethod.Post, $"api/public/products/{productKey}/tickets", form, cancellationToken);
    }
}

using Microsoft.Net.Http.Headers;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tickets;

/// <summary>
/// <c>GET /t/{token}/attachments/{id}</c>: the exempt pass-through adapter of D-017 for a customer. A browser link cannot carry the <c>X-Ticket-Token</c> header, so the Portal asks
/// <c>GET api/customer/attachments/{id}</c> for the visitor, with the token from the path as the header, and copies the answer through without buffering it. It runs no workflow and holds no data. Review Focus 2: a token
/// that is not a token, an id that is not a GUID, and an upstream not-found are the same empty 404, which the host re-executes into the one neutral not-found page, so nothing tells a wrong id from a wrong token or a
/// revoked one. The file is always a download whatever the API called it (<c>Content-Disposition: attachment</c> with a cleaned name, <c>nosniff</c>), and the host's header rules add <c>no-store</c>,
/// <c>no-referrer</c>, <c>noindex</c> and the sandbox CSP to the response (they apply to everything under <c>/t</c>, and the sandbox only to a 2xx here). A transport failure or any other upstream failure is a 502; the API's
/// 429 is a 429.
/// </summary>
public static class AttachmentPassThrough
{
    public static IEndpointRouteBuilder MapAttachmentPassThrough(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(PortalRoutes.TicketAttachmentTemplate, StreamAsync);
        return endpoints;
    }

    private static async Task StreamAsync(string token, string id, ICustomerTicketClient tickets, HttpContext http, CancellationToken cancellationToken)
    {
        if (!TicketToken.TryParse(token, out var ticketToken) || !Guid.TryParseExact(id, "D", out var attachmentId))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var result = await tickets.OpenAttachmentAsync(ticketToken, attachmentId, cancellationToken);
        if (result.IsFailure)
        {
            http.Response.StatusCode = StatusFor(result.Errors[0]);
            return;
        }

        await using var download = result.Value;
        var response = http.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = download.ContentType;
        if (download.ContentLength is { } length)
        {
            response.ContentLength = length;
        }

        response.Headers[HeaderNames.ContentDisposition] = AttachmentDisposition.Create(download.FileName);
        response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        await download.Body.CopyToAsync(response.Body, cancellationToken);
    }

    private static int StatusFor(ResultError error) =>
        error.Kind == ResultErrorKind.NotFound ? StatusCodes.Status404NotFound
        : error.Code == ApiErrorCodes.RateLimited ? StatusCodes.Status429TooManyRequests
        : StatusCodes.Status502BadGateway;
}

/// <summary>The <c>Content-Disposition</c> of a download: always <c>attachment</c> (never <c>inline</c>), with the cleaned file name (non-ASCII names become <c>filename*</c>), whatever the API sent.</summary>
public static class AttachmentDisposition
{
    public static string Create(string? fileName)
    {
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(AttachmentFileName.Clean(fileName));
        return disposition.ToString();
    }
}

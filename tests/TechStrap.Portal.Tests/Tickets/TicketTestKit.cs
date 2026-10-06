using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Tests.Forms;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// What the ticket host tests share: a ticket and its product behind the stub API, the token of the link, the antiforgery value a browser would get from the page, and the multipart post of the reply form (inputs
/// <c>Reply.Body</c> and <c>Reply.Files</c>, the handler name <c>reply</c> and the token as extra fields).
/// </summary>
internal static class TicketTestKit
{
    // Exactly 43 base64url characters, the shape of every access token.
    public const string Token = "AbC-_0123456789AbC-_0123456789AbC-_01234567";
    public const string OtherToken = "Zk9_-qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq";
    public const string TicketApi = "/api/customer/ticket";
    public const string ReplyApi = "/api/customer/ticket/replies";
    public static readonly Guid AttachmentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    public static readonly string Path = "/t/" + Token;
    public static readonly string AttachmentApi = $"/api/customer/attachments/{AttachmentId}";

    public static CustomerTicketDto Ticket(string status = "Open", string productKey = "paperplane", string subject = "Printer jam", string agentName = "Sam from Paperplane Support", string agentBody = "<p>Try <b>this</b> first.</p>", string fileName = "log.txt") =>
        new(
            "PAP-42", productKey, subject, status, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
            [
                new CustomerMessageDto(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "Requester", null, "<p>It jams every time.</p>", new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), []),
                new CustomerMessageDto(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "Agent", agentName, agentBody, new DateTimeOffset(2026, 10, 1, 10, 30, 0, TimeSpan.Zero), [new AttachmentDto(AttachmentId, fileName, "text/plain", 2048)]),
                new CustomerMessageDto(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), "System", null, "<p>Status changed.</p>", new DateTimeOffset(2026, 10, 1, 11, 0, 0, TimeSpan.Zero), []),
            ]);

    /// <summary>A host (see <see cref="FormTestKit.Factory"/>) with the ticket and the paperplane product configured.</summary>
    public static PortalFactory Factory(CustomerTicketDto? ticket = null, string environment = "Development", Action<IServiceCollection>? configure = null, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var factory = FormTestKit.Factory(environment, configure, settings);
        factory.Api.OnJson(HttpMethod.Get, TicketApi, ticket ?? Ticket());
        return factory;
    }

    public static HttpClient Client(PortalFactory factory) => FormTestKit.Client(factory);

    public static MultipartFormDataContent ReplyForm(string? antiforgery, string? body = "Still broken.", params PostedFile[] files)
    {
        var form = new MultipartFormDataContent { { new StringContent("reply"), "_handler" } };
        if (antiforgery is not null)
        {
            form.Add(new StringContent(antiforgery), "__RequestVerificationToken");
        }

        if (body is not null)
        {
            form.Add(new StringContent(body), "Reply.Body");
        }

        foreach (var file in files)
        {
            var part = new ByteArrayContent(file.Bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            form.Add(part, "Reply.Files", file.Name);
        }

        return form;
    }

    /// <summary>Every place the token text occurs in a page, with the eleven characters before it (the attribute: <c>action="/t/</c> or <c>href="/t/</c>), so a test can say it occurs nowhere else.</summary>
    public static IReadOnlyList<string> TokenContexts(string html, string token = Token) =>
        [.. Regex.Matches(html, ".{0,11}" + Regex.Escape(token)).Select(m => m.Value)];
}

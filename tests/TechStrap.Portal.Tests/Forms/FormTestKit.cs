using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>A file a test posts: its name as the browser sends it, its bytes and its content type.</summary>
internal sealed record PostedFile(string Name, byte[] Bytes, string ContentType = "text/plain");

/// <summary>
/// What the form host tests share: a product behind the stub API, the antiforgery token a real browser would get from the page, and the multipart post a browser would send for the contact form (the inputs are
/// named <c>Form.Email</c> and so on, with the handler name and the token as extra fields). The client keeps cookies, so the antiforgery cookie from the first GET goes with the post.
/// </summary>
internal static class FormTestKit
{
    public const string Path = "/p/paperplane/contact";
    public const string ReceivedPath = "/p/paperplane/contact/received";
    public const string ApiTicketsPath = "/api/public/products/paperplane/tickets";

    /// <summary>The visitor every host test pretends to be (a documentation address, RFC 5737).</summary>
    public const string Visitor = "203.0.113.9";

    public static PublicProductDto Product(string name = "Paperplane") => new("paperplane", name, null, "#F59E0B", "#000000", "#9D6507");

    /// <summary>
    /// A host behind a trusted reverse proxy (the connection's peer is the proxy) whose every API call must carry <see cref="Visitor"/> in <c>X-Forwarded-For</c>: the factory asserts it when it is disposed
    /// (<see cref="PortalFactory.ExpectedClientIp"/>). With <paramref name="product"/> the paperplane product is configured, so a page under /p/paperplane loads.
    /// </summary>
    public static PortalFactory Factory(string environment = "Development", Action<IServiceCollection>? configure = null, IReadOnlyDictionary<string, string?>? settings = null, bool product = true)
    {
        var factory = new PortalFactory(
            environment,
            settings,
            services =>
            {
                ProxyHopStartupFilter.Add("192.0.2.10")(services);
                configure?.Invoke(services);
            })
        {
            ExpectedClientIp = Visitor,
        };
        if (product)
        {
            factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Product());
        }

        return factory;
    }

    /// <summary>A client that does not follow redirects and sends the visitor's address the way the reverse proxy would.</summary>
    public static HttpClient Client(PortalFactory factory)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", Visitor);
        return client;
    }

    /// <summary>The value of the antiforgery field of the page at <paramref name="path"/>; the cookie that goes with it stays in the client.</summary>
    public static async Task<string> TokenAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        var html = await client.GetStringAsync(path, cancellationToken);
        return TokenFrom(html);
    }

    public static string TokenFrom(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        match.Success.ShouldBeTrue("the page must carry an antiforgery field");
        return match.Groups[1].Value;
    }

    public static MultipartFormDataContent ContactForm(
        string? token, string? email = "ada@example.com", string? name = "Ada Lovelace", string? subject = "Printer jam", string? body = "It jams every time.", string? website = null, params PostedFile[] files)
    {
        var form = new MultipartFormDataContent { { new StringContent("contact"), "_handler" } };
        if (token is not null)
        {
            form.Add(new StringContent(token), "__RequestVerificationToken");
        }

        Add(form, "Form.Name", name);
        Add(form, "Form.Email", email);
        Add(form, "Form.Subject", subject);
        Add(form, "Form.Body", body);
        Add(form, "Form.Website", website);
        foreach (var file in files)
        {
            var part = new ByteArrayContent(file.Bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            form.Add(part, "Form.Files", file.Name);
        }

        return form;
    }

    public static SubmitTicketResponse Created(string number = "PAP-42") => new(number, null, []);

    private static void Add(MultipartFormDataContent form, string name, string? value)
    {
        if (value is not null)
        {
            form.Add(new StringContent(value), name);
        }
    }

    /// <summary>The text between two markers, for the few places a test reads a value back (a redirect's query, an input's value).</summary>
    public static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        from.ShouldBeGreaterThanOrEqualTo(0, $"'{start}' was not found");
        from += start.Length;
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        to.ShouldBeGreaterThan(from, $"'{end}' was not found after '{start}'");
        return text[from..to];
    }
}

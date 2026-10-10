using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tickets;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// P09-T11 at the host: <c>GET /t/{token}/attachments/{id}</c>, the pass-through adapter (D-017). It streams the API's file for the visitor with the token as the header of the API call, always as a download
/// (<c>Content-Disposition: attachment</c>, <c>nosniff</c>), under the ticket headers and the sandbox policy; an upstream 404 is the uniform 404, the API's 429 is a 429 and every other failure, a transport failure
/// included, is a 502. A bad token or an id that is not a GUID is the uniform 404 and the API is not asked (the uniform-page comparison is in <c>TicketUniformNotFoundHostTests</c>).
/// </summary>
public sealed class TicketAttachmentHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly string Url = $"/t/{TicketTestKit.Token}/attachments/{TicketTestKit.AttachmentId}";
    private const string Visitor = FormTestKit.Visitor;

    private static PortalFactory Host(Action<PortalFactory>? configure = null)
    {
        var factory = FormTestKit.Factory(product: false);
        configure?.Invoke(factory);
        return factory;
    }

    private static async Task<HttpResponseMessage> GetAsync(PortalFactory factory, string? url = null)
    {
        using var client = FormTestKit.Client(factory);
        return await client.GetAsync(url ?? Url, HttpCompletionOption.ResponseContentRead, Ct);
    }

    private static string[] Policy(HttpResponseMessage response) => response.Headers.GetValues("Content-Security-Policy").Single().Split(';', StringSplitOptions.TrimEntries);

    [Fact]
    public async Task The_file_is_streamed_with_the_token_as_a_header_through_the_read_client_and_the_visitors_address()
    {
        await using var factory = Host(f => f.Api.OnFile(HttpMethod.Get, TicketTestKit.AttachmentApi, "the log text"u8.ToArray(), "text/plain", "log.txt"));

        using var response = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("the log text");
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        response.Content.Headers.ContentLength.ShouldBe(12);
        var sent = factory.Api.Requests.ShouldHaveSingleItem();
        sent.Path.ShouldBe(TicketTestKit.AttachmentApi);
        sent.TicketToken.ShouldBe(TicketTestKit.Token);
        sent.Client.ShouldBe(ApiClientNames.Read);
        sent.Query.ShouldBeEmpty();
        factory.Api.AssertEveryCallBore(Visitor);
    }

    [Fact]
    public async Task The_response_is_a_download_nosniff_no_store_noindex_no_referrer_and_sandboxed_on_top_of_the_page_policy()
    {
        await using var factory = Host(f => f.Api.OnFile(HttpMethod.Get, TicketTestKit.AttachmentApi, [1, 2, 3], "application/pdf", "report.pdf"));

        using var response = await GetAsync(factory);

        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe("report.pdf");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        Policy(response).Count(d => d == "sandbox").ShouldBe(1);
        Policy(response).ShouldContain("script-src 'self'", "the page policy is still there, with sandbox on top");
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xhtml+xml")]
    [InlineData("text/html; charset=utf-8")]
    public async Task A_type_a_browser_would_run_is_still_a_download_under_a_sandbox_with_nosniff(string contentType)
    {
        await using var factory = Host(f => f.Api.OnFile(HttpMethod.Get, TicketTestKit.AttachmentApi, "<script>alert(1)</script>"u8.ToArray(), contentType, "page.html"));

        using var response = await GetAsync(factory);

        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment", "never inline");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        Policy(response).ShouldContain("sandbox");
    }

    [Fact]
    public async Task A_disposition_the_api_sent_as_inline_is_never_passed_on()
    {
        await using var factory = Host(f => f.Api.On(HttpMethod.Get, TicketTestKit.AttachmentApi, _ =>
        {
            var upstream = StubApiHandler.FileResponse(new MemoryStream([1]), "image/png");
            upstream.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("inline") { FileName = "x.png" };
            return upstream;
        }));

        using var response = await GetAsync(factory);

        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe("x.png");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
    }

    [Fact]
    public async Task A_hostile_upstream_file_name_is_cleaned_before_it_reaches_the_content_disposition_header()
    {
        // The API's filename* is decoded by the client, so CR/LF, quotes and a right-to-left override can arrive in ApiDownload.FileName; none of them may reach the browser.
        const string Hostile = "ev\r\nil\u202Efdp.exe\"x.txt";
        await using var factory = Host(f => f.Api.On(HttpMethod.Get, TicketTestKit.AttachmentApi, _ =>
        {
            var upstream = StubApiHandler.FileResponse(new MemoryStream([1]), "application/octet-stream");
            upstream.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("inline") { FileNameStar = Hostile };
            return upstream;
        }));

        using var response = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = response.Content.Headers.GetValues("Content-Disposition").ShouldHaveSingleItem();
        raw.ShouldStartWith("attachment");
        raw.ShouldNotContain("\r");
        raw.ShouldNotContain("\n");
        raw.ShouldNotContain("\u202E");
        raw.ShouldNotContain("\"");
        raw.ShouldNotContain("%0D", Case.Insensitive);
        raw.ShouldNotContain("%0A", Case.Insensitive);
        raw.ShouldNotContain("%E2%80%AE", Case.Insensitive);
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileNameStar.ShouldBe("evilfdp.exex.txt");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Fact]
    public async Task The_endpoint_itself_forces_a_clean_attachment_and_nosniff_without_the_shared_header_middleware()
    {
        // Only the pass-through is mapped: no security-header middleware can add or overwrite anything, so what arrives is the endpoint's own doing (defense in depth, not left to the host's rules).
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ICustomerTicketClient>(new HostileFileClient());
        await using var app = builder.Build();
        app.MapAttachmentPassThrough();
        await app.StartAsync(Ct);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(Url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        var raw = response.Content.Headers.GetValues("Content-Disposition").ShouldHaveSingleItem();
        raw.ShouldStartWith("attachment");
        raw.ShouldNotContain("\r");
        raw.ShouldNotContain("\n");
        raw.ShouldNotContain("\u202E");
        raw.ShouldNotContain("\"");
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("evilfdp.exex.txt");
    }

    [Fact]
    public async Task The_download_is_disposed_when_the_endpoint_is_done_with_it()
    {
        var client = new TrackingFileClient();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ICustomerTicketClient>(client);
        await using var app = builder.Build();
        app.MapAttachmentPassThrough();
        await app.StartAsync(Ct);
        using var http = app.GetTestClient();

        using var response = await http.GetAsync(Url, Ct);
        await response.Content.ReadAsByteArrayAsync(Ct);

        // The endpoint has finished writing; its download (the upstream body and response) must have been released, not left to the garbage collector.
        for (var i = 0; i < 100 && !client.Body.Closed; i++)
        {
            await Task.Delay(20, Ct);
        }

        client.Body.Closed.ShouldBeTrue("the endpoint must dispose the download it opened");
        client.Content.Disposed.ShouldBeTrue("and with it the upstream response");
    }

    private sealed class TrackingBody : MemoryStream
    {
        public TrackingBody()
            : base([1, 2, 3])
        {
        }

        public bool Closed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackedContent(Stream body) : StreamContent(body)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingFileClient : ICustomerTicketClient
    {
        public TrackingBody Body { get; } = new();

        public TrackedContent Content { get; private set; } = null!;

        public Task<Result<ApiDownload>> OpenAttachmentAsync(TicketToken token, Guid attachmentId, CancellationToken cancellationToken)
        {
            Content = new TrackedContent(Body);
            var upstream = new HttpResponseMessage(HttpStatusCode.OK) { Content = Content };
            return Task.FromResult(Result<ApiDownload>.Success(new ApiDownload(new HttpRequestMessage(), upstream, Body)));
        }

        public Task<Result<CustomerTicketDto>> GetAsync(TicketToken token, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CustomerReplyResponse>> ReplyAsync(TicketToken token, CustomerReply reply, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result> RequestAccessLinkAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class HostileFileClient : ICustomerTicketClient
    {
        public Task<Result<ApiDownload>> OpenAttachmentAsync(TicketToken token, Guid attachmentId, CancellationToken cancellationToken)
        {
            var upstream = StubApiHandler.FileResponse(new MemoryStream([1]), "text/html");
            upstream.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("inline") { FileNameStar = "ev\r\nil\u202Efdp.exe\"x.txt" };
            return Task.FromResult(Result<ApiDownload>.Success(new ApiDownload(new HttpRequestMessage(), upstream, upstream.Content.ReadAsStream(cancellationToken))));
        }

        public Task<Result<CustomerTicketDto>> GetAsync(TicketToken token, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<CustomerReplyResponse>> ReplyAsync(TicketToken token, CustomerReply reply, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result> RequestAccessLinkAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_file_with_no_name_is_a_download_called_attachment()
    {
        await using var factory = Host(f => f.Api.On(HttpMethod.Get, TicketTestKit.AttachmentApi, _ => StubApiHandler.FileResponse(new MemoryStream([1]), "application/octet-stream")));

        using var response = await GetAsync(factory);

        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe("attachment");
    }

    [Fact]
    public async Task A_big_file_arrives_complete()
    {
        var bytes = new byte[5 * 1024 * 1024];
        new Random(7).NextBytes(bytes);
        await using var factory = Host(f => f.Api.OnFile(HttpMethod.Get, TicketTestKit.AttachmentApi, bytes, "application/zip", "big.zip"));

        using var response = await GetAsync(factory);

        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(bytes);
    }

    [Fact]
    public async Task An_upstream_404_is_the_uniform_404_not_sandboxed_and_with_the_ticket_headers()
    {
        await using var factory = Host(f => f.Api.OnProblem(HttpMethod.Get, TicketTestKit.AttachmentApi, HttpStatusCode.NotFound, "attachment-not-found", "No such attachment."));

        using var response = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("Page not found");
        Policy(response).ShouldNotContain("sandbox", "the not-found page is an ordinary page");
        response.Headers.GetValues("X-Robots-Tag").Single().ShouldBe("noindex");
        response.Content.Headers.ContentDisposition.ShouldBeNull();
    }

    [Theory]
    [InlineData("/t/short/attachments/11111111-2222-3333-4444-555555555555")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567/attachments/not-a-guid")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567/attachments/11111111222233334444555555555555")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567/attachments/%7B11111111-2222-3333-4444-555555555555%7D")]
    public async Task A_bad_token_or_an_id_that_is_not_a_guid_is_a_404_and_the_api_is_never_asked(string path)
    {
        await using var factory = Host(f => f.Api.OnFile(HttpMethod.Get, TicketTestKit.AttachmentApi, [1], "text/plain", "x.txt"));

        using var response = await GetAsync(factory, path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_apis_429_is_a_429()
    {
        await using var factory = Host(f => f.Api.OnStatus(HttpMethod.Get, TicketTestKit.AttachmentApi, HttpStatusCode.TooManyRequests));

        using var response = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Any_other_upstream_failure_is_a_502_with_nothing_from_the_api(HttpStatusCode upstream)
    {
        await using var factory = Host(f => f.Api.OnProblem(HttpMethod.Get, TicketTestKit.AttachmentApi, upstream, "x", "Npgsql host=10.0.0.5"));

        using var response = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("Npgsql");
    }

    [Fact]
    public async Task A_transport_failure_is_a_502()
    {
        await using var factory = Host(f => f.Api.On(HttpMethod.Get, TicketTestKit.AttachmentApi, _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)")));

        using var response = await GetAsync(factory);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Only_a_get_is_answered()
    {
        await using var factory = Host(f => f.Api.OnFile(HttpMethod.Get, TicketTestKit.AttachmentApi, [1], "text/plain", "x.txt"));
        using var client = FormTestKit.Client(factory);

        using var response = await client.PostAsync(Url, new StringContent("x"), Ct);

        response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        factory.Api.Requests.ShouldBeEmpty();
    }

    // ---- the disposition header alone ----

    [Theory]
    [InlineData("report.pdf", "attachment; filename=report.pdf; filename*=UTF-8''report.pdf")]
    [InlineData("my report.pdf", "attachment; filename=\"my report.pdf\"; filename*=UTF-8''my%20report.pdf")]
    [InlineData(null, "attachment; filename=attachment; filename*=UTF-8''attachment")]
    [InlineData("", "attachment; filename=attachment; filename*=UTF-8''attachment")]
    [InlineData("C:\\x\\a.txt", "attachment; filename=a.txt; filename*=UTF-8''a.txt")]
    [InlineData("a\"b.txt", "attachment; filename=ab.txt; filename*=UTF-8''ab.txt")]
    public void The_disposition_is_always_attachment_with_the_cleaned_name(string? name, string expected) => AttachmentDisposition.Create(name).ShouldBe(expected);

    [Fact]
    public void A_name_that_could_split_a_header_cannot()
    {
        var header = AttachmentDisposition.Create("evil\r\nSet-Cookie: x=1.txt");

        header.ShouldNotContain("\r");
        header.ShouldNotContain("\n");
        header.ShouldStartWith("attachment");
    }

    [Fact]
    public void A_right_to_left_override_and_quotes_are_removed_from_the_name()
    {
        var header = AttachmentDisposition.Create("fil\u202Efdp.exe\"");

        header.ShouldNotContain("\u202E");
        header.ShouldNotContain("%E2%80%AE", Case.Insensitive);
        header.ShouldStartWith("attachment; filename=filfdp.exe;");
    }

    [Fact]
    public void A_non_ascii_name_travels_as_an_encoded_filename_star()
    {
        var header = AttachmentDisposition.Create("caf" + (char)0xE9 + ".pdf");

        header.ShouldContain("filename*=UTF-8''caf%C3%A9.pdf");
        header.ShouldStartWith("attachment");
    }
}

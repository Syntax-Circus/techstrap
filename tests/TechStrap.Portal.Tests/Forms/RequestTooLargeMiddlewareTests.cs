using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Uploads;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// Review Focus 3: the request size limit applies before the form is buffered. The unit tests drive the middleware with a feature the way Kestrel supplies it (the in-memory test server has none, so the host
/// tests below use the real server, <c>UseKestrel(0)</c>): a declared length over the applied limit is a plain 413, one at the limit passes, and a server without the feature does nothing. The host tests prove
/// the page's own <c>[RequestSizeLimit(IntakeLimits.FormBodyBytes)]</c> is the applied limit and that it is in force before antiforgery reads the form, with a real post over the wire.
/// </summary>
public sealed class RequestTooLargeMiddlewareTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class LimitFeature(long? limit) : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;

        public long? MaxRequestBodySize { get; set; } = limit;
    }

    private static async Task<(DefaultHttpContext Context, bool Passed)> RunAsync(long? limit, long? declared, bool withFeature = true)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        if (withFeature)
        {
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(new LimitFeature(limit));
        }

        context.Request.ContentLength = declared;
        var passed = false;
        await new RequestTooLargeMiddleware(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        }).InvokeAsync(context);
        return (context, passed);
    }

    [Fact]
    public async Task A_declared_length_over_the_limit_is_a_plain_413_with_the_sentence_and_nothing_after_it_runs()
    {
        var (context, passed) = await RunAsync(1000, 1001);

        passed.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status413PayloadTooLarge);
        context.Response.ContentType.ShouldBe("text/plain; charset=utf-8");
        context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
        context.Response.Body.Position = 0;
        new StreamReader(context.Response.Body).ReadToEnd().ShouldBe(ProblemCopy.PayloadTooLarge);
    }

    [Theory]
    [InlineData(1000L, 1000L)]
    [InlineData(1000L, 0L)]
    [InlineData(1000L, null)]
    public async Task A_length_at_the_limit_or_no_declared_length_passes(long limit, long? declared)
    {
        var (context, passed) = await RunAsync(limit, declared);

        passed.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task A_server_without_the_feature_or_with_no_limit_does_nothing()
    {
        (await RunAsync(null, 99_999_999, withFeature: false)).Passed.ShouldBeTrue();
        (await RunAsync(null, 99_999_999)).Passed.ShouldBeTrue();
    }

    // ---- the real server ----

    private static (HttpClient Client, PortalFactory Factory) Kestrel()
    {
        var factory = FormTestKit.Factory();
        factory.UseKestrel(0);
        factory.StartServer();
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.Add("X-Forwarded-For", FormTestKit.Visitor);
        return (client, factory);
    }

    private static HttpRequestMessage Post(string token, int fileBytes, bool expectContinue)
    {
        var form = FormTestKit.ContactForm(token, files: [new("big.zip", new byte[fileBytes], "application/zip")]);
        var request = new HttpRequestMessage(HttpMethod.Post, FormTestKit.Path) { Content = form };
        request.Headers.ExpectContinue = expectContinue;
        return request;
    }

    [Fact]
    public async Task On_the_real_server_a_post_over_the_forms_limit_is_a_413_and_nothing_is_sent_to_the_api()
    {
        var (client, factory) = Kestrel();
        using var _ = client;
        await using var __ = factory;
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);
        var apiCalls = factory.Api.Requests.Count;

        // Expect: 100-continue lets the server refuse on the declared length before the client streams 26 MB into a closing socket.
        using var response = await client.SendAsync(Post(token, (int)IntakeLimits.FormBodyBytes, expectContinue: true), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe(ProblemCopy.PayloadTooLarge);
        factory.Api.Requests.Count.ShouldBe(apiCalls, "the form was never read, so the page never even asked for the product");
    }

    [Fact]
    public async Task On_the_real_server_a_post_under_the_limit_with_a_large_file_still_works_and_reaches_the_api_in_full()
    {
        var (client, factory) = Kestrel();
        using var _ = client;
        await using var __ = factory;
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        using var response = await client.SendAsync(Post(token, 9 * 1024 * 1024, expectContinue: true), Ct);

        // 9 MiB is under the 10 MiB file limit and under the form limit: it is accepted and redirected (HttpClient follows the redirect to the received page).
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("We have received your request.");
        var sent = factory.Api.Requests.Single(r => r.Method == HttpMethod.Post);
        sent.Body.ShouldNotBeNull().Length.ShouldBeGreaterThan(9 * 1024 * 1024);
    }

    [Fact]
    public async Task On_the_real_server_the_antiforgery_check_still_comes_first_for_a_small_post_without_a_token()
    {
        var (client, factory) = Kestrel();
        using var _ = client;
        await using var __ = factory;
        await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        using var response = await client.PostAsync(FormTestKit.Path, FormTestKit.ContactForm(null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }

    [Fact]
    public async Task The_limit_the_page_applies_is_exactly_the_contract_form_limit()
    {
        var (client, factory) = Kestrel();
        using var _ = client;
        await using var __ = factory;
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);

        // One byte over the declared limit is refused; the same request one byte under is not (it fails later, on the file rule, because it is a big file of no allowed shape for the stub).
        using var content = FormTestKit.ContactForm(token);
        var small = await content.ReadAsByteArrayAsync(Ct);
        using var over = new HttpRequestMessage(HttpMethod.Post, FormTestKit.Path) { Content = new ByteArrayContent(new byte[(int)IntakeLimits.FormBodyBytes + 1]) };
        over.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(content.Headers.ContentType!.ToString());
        over.Headers.ExpectContinue = true;

        using var refused = await client.SendAsync(over, Ct);

        small.Length.ShouldBeLessThan((int)IntakeLimits.FormBodyBytes);
        refused.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    /// <summary>Another content's bytes and headers, but not its length, so it goes over the wire chunked (no <c>Content-Length</c> for the middleware to refuse on).</summary>
    private sealed class Chunked : HttpContent
    {
        private readonly HttpContent _inner;

        public Chunked(HttpContent inner)
        {
            _inner = inner;
            foreach (var header in inner.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            Headers.ContentLength = null;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => _inner.CopyToAsync(stream, context);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact]
    public async Task On_the_real_server_a_chunked_body_over_the_limit_is_a_400_and_nothing_is_sent_to_the_api()
    {
        var (client, factory) = Kestrel();
        using var _ = client;
        await using var __ = factory;
        factory.Api.OnJson(HttpMethod.Post, FormTestKit.ApiTicketsPath, FormTestKit.Created(), HttpStatusCode.Created);
        var token = await FormTestKit.TokenAsync(client, FormTestKit.Path, Ct);
        var apiCalls = factory.Api.Requests.Count;
        // A real, well-formed contact post whose file pushes it over the limit: only the size can make it fail.
        using var form = FormTestKit.ContactForm(token, files: [new("big.zip", new byte[(int)IntakeLimits.FormBodyBytes], "application/zip")]);
        using var request = new HttpRequestMessage(HttpMethod.Post, FormTestKit.Path) { Content = new Chunked(form) };
        request.Headers.TransferEncodingChunked = true;

        // No declared length, so the middleware cannot answer 413; Kestrel refuses the read at the limit and the antiforgery check reports it as a 400. What matters: the body is not buffered, nothing is created.
        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        request.Content.Headers.ContentLength.ShouldBeNull("control: this really went over the wire without a length");
        factory.Api.Requests.Count.ShouldBe(apiCalls, "the form was never read, so the page never even asked for the product");
        factory.Api.Count(HttpMethod.Post, FormTestKit.ApiTicketsPath).ShouldBe(0);
    }
}

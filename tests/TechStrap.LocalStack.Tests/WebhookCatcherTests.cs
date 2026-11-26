using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.LocalStack.Tests;

/// <summary>The local webhook catcher: what it stores, what it shows, and who may call it.</summary>
public sealed class WebhookCatcherTests
{
    // The header names the Worker sends (WebhookCaptureRegistration and WebhookSender), written out because the catcher does not reference src/.
    private const string OriginalHostHeader = "X-TechStrap-Capture-Original-Host";
    private const string EventIdHeader = "X-TechStrap-Event-Id";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private sealed record Captured(DateTimeOffset ReceivedAt, string OriginalHost, string? EventId, string ContentType, string Body);

    private static WebApplicationFactory<TechStrap.WebhookCatcher.Program> CreateFactory() => new();

    private static async Task<HttpResponseMessage> PostCaptureAsync(HttpClient client, HttpContent content, string? originalHost = null, string? eventId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/capture") { Content = content };
        if (originalHost is not null)
        {
            request.Headers.Add(OriginalHostHeader, originalHost);
        }

        if (eventId is not null)
        {
            request.Headers.Add(EventIdHeader, eventId);
        }

        return await client.SendAsync(request, Cancellation);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<Captured[]> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Captured[]>("/api/requests", Cancellation)).ShouldNotBeNull();

    [Fact]
    public async Task Capture_stores_host_event_id_and_body()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var before = DateTimeOffset.UtcNow;

        using var response = await PostCaptureAsync(client, Json("""{"title":"Zoë"}"""), "hooks.example.com", "7f1c0000-0000-0000-0000-000000000001");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var captured = (await ListAsync(client)).ShouldHaveSingleItem();
        captured.OriginalHost.ShouldBe("hooks.example.com");
        captured.EventId.ShouldBe("7f1c0000-0000-0000-0000-000000000001");
        captured.ContentType.ShouldBe("application/json; charset=utf-8");
        captured.Body.ShouldBe("""{"title":"Zoë"}""");
        captured.ReceivedAt.ShouldBeInRange(before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Missing_headers_are_stored_as_empty_host_and_no_event_id()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await PostCaptureAsync(client, Json("{}"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var captured = (await ListAsync(client)).ShouldHaveSingleItem();
        captured.OriginalHost.ShouldBeEmpty();
        captured.EventId.ShouldBeNull();
    }

    [Fact]
    public async Task Only_the_newest_200_are_kept()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        for (var i = 1; i <= 201; i++)
        {
            using var response = await PostCaptureAsync(client, Json($"{i}"));
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var captured = await ListAsync(client);
        captured.Select(c => c.Body).ShouldBe(Enumerable.Range(2, 200).Reverse().Select(i => $"{i}"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Oversized_body_is_rejected(bool declaresLength)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var content = Body(65537, declaresLength);

        using var response = await PostCaptureAsync(client, content);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await ListAsync(client)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Body_at_the_limit_is_stored(bool declaresLength)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var content = Body(65536, declaresLength);

        using var response = await PostCaptureAsync(client, content);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(client)).ShouldHaveSingleItem().Body.Length.ShouldBe(65536);
    }

    [Fact]
    public async Task Page_encodes_captured_values()
    {
        const string Hostile = "<script>alert(1)</script>";
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent(Hostile, Encoding.UTF8);
        content.Headers.Remove("Content-Type");
        content.Headers.TryAddWithoutValidation("Content-Type", Hostile).ShouldBeTrue();
        using var response = await PostCaptureAsync(client, content, Hostile, Hostile);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var page = await client.GetAsync("/", Cancellation);
        var html = await page.Content.ReadAsStringAsync(Cancellation);

        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("text/html");
        html.ShouldNotContain("<script>alert", Case.Insensitive);
        // Host, event id, content type and body: each is shown, as text.
        html.Split("&lt;script&gt;").Length.ShouldBe(5);
    }

    [Fact]
    public async Task Clear_empties_the_list()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var captured = await PostCaptureAsync(client, Json("{}"));
        (await ListAsync(client)).ShouldHaveSingleItem();

        // The page's button: a form post that lands back on the list.
        using var form = new FormUrlEncodedContent([]);
        using var response = await client.PostAsync("/clear", form, Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.RequestMessage.ShouldNotBeNull().RequestUri.ShouldNotBeNull().AbsolutePath.ShouldBe("/");
        (await ListAsync(client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Health_answers_ok()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Refuses_to_start_outside_Development(string environment)
    {
        using var root = CreateFactory();
        using var factory = root.WithWebHostBuilder(builder => builder.UseEnvironment(environment));

        var failure = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        failure.Message.ShouldContain("only in Development");
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("203.0.113.7")]
    [InlineData("::ffff:172.17.0.2")]
    [InlineData("2001:db8::1")]
    public async Task Non_loopback_callers_get_403(string address)
    {
        using var root = CreateFactory();
        using var factory = CallingFrom(root, IPAddress.Parse(address));
        using var client = factory.CreateClient();

        using var content = Json("{}");
        using var post = await PostCaptureAsync(client, content);
        using var page = await client.GetAsync("/", Cancellation);
        using var list = await client.GetAsync("/api/requests", Cancellation);
        using var clear = await client.PostAsync("/clear", content: null, Cancellation);
        using var health = await client.GetAsync("/health", Cancellation);

        new[] { post, page, list, clear, health }.Select(r => r.StatusCode).ShouldAllBe(status => status == HttpStatusCode.Forbidden);
        factory.Services.GetRequiredService<TechStrap.WebhookCatcher.CaptureStore>().Snapshot().ShouldBeEmpty();
    }

    // Loopback binding alone does not stop a web page that rebinds its own name to 127.0.0.1; the Host header gives it away.
    [Theory]
    [InlineData("attacker.example")]
    [InlineData("attacker.example:55270")]
    [InlineData("127.0.0.1.nip.io:55270")]
    public async Task Foreign_host_headers_get_400(string host)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var post = await SendWithHostAsync(client, HttpMethod.Post, "/capture", host, Json("{}"));
        using var list = await SendWithHostAsync(client, HttpMethod.Get, "/api/requests", host);

        post.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        list.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        factory.Services.GetRequiredService<TechStrap.WebhookCatcher.CaptureStore>().Snapshot().ShouldBeEmpty();
    }

    // The Worker posts to 127.0.0.1; a browser and Aspire's health check use localhost.
    [Theory]
    [InlineData("127.0.0.1:55270")]
    [InlineData("localhost:55270")]
    [InlineData("[::1]:55270")]
    public async Task Local_host_headers_are_accepted(string host)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await SendWithHostAsync(client, HttpMethod.Post, "/capture", host, Json("{}"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<HttpResponseMessage> SendWithHostAsync(HttpClient client, HttpMethod method, string path, string host, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Host = host;
        return await client.SendAsync(request, Cancellation);
    }

    // A null address (TestServer's own) is what every other test here uses.
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task Loopback_callers_are_allowed(string address)
    {
        using var root = CreateFactory();
        using var factory = CallingFrom(root, IPAddress.Parse(address));
        using var client = factory.CreateClient();

        using var content = Json("{}");
        using var response = await PostCaptureAsync(client, content);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static HttpContent Body(int bytes, bool declaresLength)
    {
        var buffer = Enumerable.Repeat((byte)'a', bytes).ToArray();
        return declaresLength ? new ByteArrayContent(buffer) : new UnknownLengthContent(buffer);
    }

    private static WebApplicationFactory<TechStrap.WebhookCatcher.Program> CallingFrom(
        WebApplicationFactory<TechStrap.WebhookCatcher.Program> root, IPAddress address) =>
        root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(address))));

    private sealed class RemoteAddressFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, following) =>
            {
                context.Connection.RemoteIpAddress = address;
                return following(context);
            });
            next(app);
        };
    }

    /// <summary>A body sent without a Content-Length, as a chunked request is.</summary>
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}

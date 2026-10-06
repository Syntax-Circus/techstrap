using Sentry;
using Sentry.Extensibility;
using Sentry.Internal;
using Sentry.Protocol;
using Sentry.Protocol.Envelopes;
using TechStrap.Hosting.Sentry;

namespace TechStrap.Api.Tests;

/// <summary>
/// Review Focus 4, Sentry: the text an agent searched for (a requester's email address, a subject line) never reaches Sentry in a request's query string or URL, in a breadcrumb, or in a span. The
/// last test sends an event through the SDK with the host's registration and reads what the transport receives, with a control that proves the harness would see a leak.
/// </summary>
/// <remarks>The last test initialises the SDK, which is process-wide state, so the class runs in the non-parallel <see cref="ProcessEnvironmentCollection"/> with the other tests that must run alone.</remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class SensitiveQuerySentryProcessorTests
{
    private const string Needle = "ada.lovelace%40orbitly.test";

    [Theory]
    [InlineData("search=ada%40x.test", "search=[redacted]")]
    [InlineData("?search=ada%40x.test&page=2", "?search=[redacted]&page=2")]
    [InlineData("?status=Open&search=ada%40x.test&page=2", "?status=Open&search=[redacted]&page=2")]
    [InlineData("?q=reset+password", "?q=[redacted]")]
    [InlineData("?q=a&search=b", "?q=[redacted]&search=[redacted]")]
    [InlineData("?SEARCH=Ada&Q=b", "?SEARCH=[redacted]&Q=[redacted]")]
    [InlineData("?search=", "?search=[redacted]")]
    [InlineData("https://admin.test/queue/mine?status=Open&search=ada@x.test&page=2#top", "https://admin.test/queue/mine?status=Open&search=[redacted]&page=2#top")]
    [InlineData("GET https://api.test/api/tickets?search=a%20b&pageSize=25 failed", "GET https://api.test/api/tickets?search=[redacted]&pageSize=25 failed")]
    [InlineData("?%73earch=ada%40x.test", "?%73earch=[redacted]")]
    [InlineData("?%53earch=x&%71=y", "?%53earch=[redacted]&%71=[redacted]")]
    [InlineData("?Search=x", "?Search=[redacted]")]
    [InlineData("theme=dark; last=/queue/mine?search=ada%40x.test", "theme=dark; last=/queue/mine?search=[redacted]")]
    [InlineData("https://admin.test/queue/mine?status=Open&%73earch=x", "https://admin.test/queue/mine?status=Open&%73earch=[redacted]")]
    public void The_value_of_search_and_q_is_masked_and_the_rest_of_the_address_is_kept(string text, string expected) =>
        SensitiveQuerySentryProcessor.Scrub(text).ShouldBe(expected);

    // P09-T17 / T21: the Portal's contact page may be opened with a name and an email in the address, and its ticket address carries the access token in the path.
    [Theory]
    [InlineData("?name=Jane%20Doe", "?name=[redacted]")]
    [InlineData("?subject=Hi&name=Jane+Doe&email=jane%40example.com&page=2", "?subject=Hi&name=[redacted]&email=[redacted]&page=2")]
    [InlineData("?NAME=Jane&Email=a@b.example", "?NAME=[redacted]&Email=[redacted]")]
    [InlineData("?%6Eame=Jane&%65mail=x", "?%6Eame=[redacted]&%65mail=[redacted]")]
    [InlineData("https://portal.test/p/orbitly/contact?name=Jane&email=jane%40example.com#top", "https://portal.test/p/orbitly/contact?name=[redacted]&email=[redacted]#top")]
    public void The_value_of_name_and_email_is_masked_so_the_contact_page_prefill_never_reaches_Sentry(string text, string expected) =>
        SensitiveQuerySentryProcessor.Scrub(text).ShouldBe(expected);

    [Theory]
    [InlineData("https://portal.test/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE", "https://portal.test/t/[token]")]
    [InlineData("https://portal.test/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE/attachments/11111111-2222-3333-4444-555555555555", "https://portal.test/t/[token]/attachments/11111111-2222-3333-4444-555555555555")]
    [InlineData("/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE?x=1", "/t/[token]?x=1")]
    [InlineData("GET /T/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE failed", "GET /T/[token] failed")]
    [InlineData("https://portal.test/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE?name=Jane", "https://portal.test/t/[token]?name=[redacted]")]
    public void An_access_token_in_a_ticket_path_is_masked_so_the_ticket_address_never_reaches_Sentry(string text, string expected) =>
        SensitiveQuerySentryProcessor.Scrub(text).ShouldBe(expected);

    [Theory]
    [InlineData("/t/short")]
    [InlineData("/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdEx")]
    [InlineData("/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCd")]
    [InlineData("/ticket/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE")]
    [InlineData("/api/customer/ticket")]
    public void Only_a_43_character_token_directly_under_t_is_masked(string text) =>
        SensitiveQuerySentryProcessor.Scrub(text).ShouldBe(text);

    [Fact]
    public void A_ticket_address_in_a_request_a_breadcrumb_and_a_span_is_masked_everywhere_the_search_is()
    {
        const string url = "https://portal.test/t/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE?name=Jane";
        var @event = new SentryEvent();
        @event.Request.Url = url;
        @event.Request.QueryString = "?name=Jane";
        @event.Request.Headers["Referer"] = url;

        var scrubbed = new SensitiveQuerySentryProcessor().Process(@event)!;
        var breadcrumb = SensitiveQuerySentryProcessor.ScrubBreadcrumb(new Breadcrumb("GET " + url, "http", new Dictionary<string, string> { ["url"] = url }, "http", BreadcrumbLevel.Info), new SentryHint())!;

        scrubbed.Request.Url.ShouldBe("https://portal.test/t/[token]?name=[redacted]");
        scrubbed.Request.QueryString.ShouldBe("?name=[redacted]");
        scrubbed.Request.Headers["Referer"].ShouldBe("https://portal.test/t/[token]?name=[redacted]");
        breadcrumb.Message.ShouldBe("GET https://portal.test/t/[token]?name=[redacted]");
        breadcrumb.Data!["url"].ShouldBe("https://portal.test/t/[token]?name=[redacted]");
    }

    [Theory]
    [InlineData("?status=Open&page=2")]
    [InlineData("?research=1&faq=2&query=3&squash=4")]
    [InlineData("/queue/search")]
    [InlineData("?rese%61rch=1&f%61q=2")]
    [InlineData("https://admin.test/queue/search?status=Open")]
    [InlineData("search")]
    [InlineData("text/html;q=0.9")]
    [InlineData("a message that says search=nothing but is not a query")]
    [InlineData("")]
    public void Anything_that_is_not_a_search_parameter_is_left_alone(string text)
    {
        // The last-but-one input does contain "search=" but not as a parameter: it follows a space, so it is not a start of a query string, a "?" or an "&".
        SensitiveQuerySentryProcessor.Scrub(text).ShouldBe(text);
    }

    [Theory]
    [InlineData("flat")]
    [InlineData("nested")]
    public void Hostile_input_is_scrubbed_in_bounded_stack_and_time(string shape)
    {
        // "x=a=a=a=..." made every value a new level of recursion, and a stack overflow cannot be caught. Run on a 256 KB stack: it must finish, quickly.
        var text = shape == "flat" ? "x" + string.Concat(Enumerable.Repeat("=a", 32 * 1024)) : string.Concat(Enumerable.Repeat("a=b?c=d&", 8 * 1024));
        string? result = null;
        Exception? failure = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var thread = new Thread(() =>
        {
            try
            {
                result = SensitiveQuerySentryProcessor.Scrub(text);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }, maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();
        watch.Stop();

        failure.ShouldBeNull();
        result.ShouldNotBeNull();
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Nothing_stays_nothing()
    {
        SensitiveQuerySentryProcessor.Scrub(null).ShouldBeNull();
    }

    [Fact]
    public void An_event_loses_the_search_from_its_query_string_and_url_and_keeps_everything_else()
    {
        var sentryEvent = new SentryEvent();
        sentryEvent.Request.QueryString = $"?status=Open&search={Needle}";
        sentryEvent.Request.Url = $"https://admin.test/queue/mine?status=Open&search={Needle}&page=2";
        sentryEvent.Request.Method = "GET";
        sentryEvent.Request.Headers["User-Agent"] = "ua";

        var result = new SensitiveQuerySentryProcessor().Process(sentryEvent);

        result.ShouldNotBeNull();
        result.Request.QueryString.ShouldBe("?status=Open&search=[redacted]");
        result.Request.Url.ShouldBe("https://admin.test/queue/mine?status=Open&search=[redacted]&page=2");
        result.Request.Method.ShouldBe("GET");
        result.Request.Headers["User-Agent"].ShouldBe("ua");
    }

    [Fact]
    public void An_event_with_no_request_is_untouched()
    {
        var result = new SensitiveQuerySentryProcessor().Process(new SentryEvent { Message = "boom" });

        result.ShouldNotBeNull();
        result.Request.QueryString.ShouldBeNull();
        result.Request.Url.ShouldBeNull();
    }

    [Fact]
    public void A_transaction_loses_the_search_from_its_request_and_from_every_span()
    {
        var tracer = new TransactionTracer(DisabledHub.Instance, new TransactionContext("GET /queue/{view}", "http.server", null, null, null, "", null, null, true, TransactionNameSource.Route));
        tracer.Request.QueryString = $"search={Needle}";
        tracer.Request.Url = $"https://admin.test/queue/mine?search={Needle}";
        var span = tracer.StartChild("http.client", $"GET https://api.test/api/tickets?search={Needle}&pageSize=25");
        span.SetData("http.query", $"?search={Needle}&pageSize=25");
        span.SetData("http.response.status_code", 200);
        span.Finish();
        var transaction = new SentryTransaction(tracer);

        var result = new SensitiveQuerySentryProcessor().Process(transaction);

        result.ShouldNotBeNull();
        result.Request.QueryString.ShouldBe("search=[redacted]");
        result.Request.Url.ShouldBe("https://admin.test/queue/mine?search=[redacted]");
        var scrubbed = result.Spans.Single();
        scrubbed.Description.ShouldBe("GET https://api.test/api/tickets?search=[redacted]&pageSize=25");
        scrubbed.Data["http.query"].ShouldBe("?search=[redacted]&pageSize=25");
        scrubbed.Data["http.response.status_code"].ShouldBe(200);
    }

    [Fact]
    public void An_event_loses_the_search_from_every_request_header_and_keeps_the_header_names()
    {
        var sentryEvent = new SentryEvent();
        sentryEvent.Request.Headers["Referer"] = $"https://admin.test/queue/mine?status=Open&search={Needle}";
        sentryEvent.Request.Headers["User-Agent"] = "ua";

        var result = new SensitiveQuerySentryProcessor().Process(sentryEvent);

        result.ShouldNotBeNull();
        result.Request.Headers["Referer"].ShouldBe("https://admin.test/queue/mine?status=Open&search=[redacted]");
        result.Request.Headers["User-Agent"].ShouldBe("ua");
    }

    [Fact]
    public void A_transaction_loses_the_search_from_its_request_headers_and_tags_and_from_span_tags()
    {
        var tracer = new TransactionTracer(DisabledHub.Instance, new TransactionContext("GET /queue/{view}", "http.server", null, null, null, "", null, null, true, TransactionNameSource.Route));
        tracer.Request.Headers["Referer"] = $"https://admin.test/queue/mine?search={Needle}";
        tracer.SetTag("page", $"/queue/mine?search={Needle}");
        var span = tracer.StartChild("http.client", "GET /api/tickets");
        span.SetTag("url", $"/api/tickets?search={Needle}");
        span.Finish();

        var result = new SensitiveQuerySentryProcessor().Process(new SentryTransaction(tracer));

        result.ShouldNotBeNull();
        result.Request.Headers["Referer"].ShouldBe("https://admin.test/queue/mine?search=[redacted]");
        result.Tags["page"].ShouldBe("/queue/mine?search=[redacted]");
        result.Spans.Single().Tags["url"].ShouldBe("/api/tickets?search=[redacted]");
    }

    [Fact]
    public void An_event_loses_the_search_from_its_message_tags_extra_and_exception_values()
    {
        var sentryEvent = new SentryEvent { Message = new SentryMessage { Message = "failed %s", Formatted = $"failed /queue/mine?search={Needle}" } };
        sentryEvent.SetTag("page", $"/queue/mine?search={Needle}");
        sentryEvent.SetExtra("url", $"/queue/mine?search={Needle}");
        sentryEvent.SetExtra("count", 3);
        sentryEvent.SentryExceptions = [new SentryException { Type = "HttpRequestException", Value = $"GET /api/tickets?search={Needle} failed" }];

        var result = new SensitiveQuerySentryProcessor().Process(sentryEvent);

        result.ShouldNotBeNull();
        result.Message!.Formatted.ShouldBe("failed /queue/mine?search=[redacted]");
        result.Tags["page"].ShouldBe("/queue/mine?search=[redacted]");
        result.Extra["url"].ShouldBe("/queue/mine?search=[redacted]");
        result.Extra["count"].ShouldBe(3);
        result.SentryExceptions!.Single().Value.ShouldBe("GET /api/tickets?search=[redacted] failed");
    }

    [Fact]
    public void An_event_loses_the_search_from_its_message_parameters_request_body_and_cookies_and_keeps_what_is_not_text()
    {
        var sentryEvent = new SentryEvent { Message = new SentryMessage { Message = "failed %s %d", Params = [$"/queue/mine?search={Needle}", 7] } };
        sentryEvent.Request.Data = $"search={Needle}&page=2";
        sentryEvent.Request.Cookies = $"theme=dark; last=/queue/mine?status=Open&search={Needle}";

        var result = new SensitiveQuerySentryProcessor().Process(sentryEvent);

        result.ShouldNotBeNull();
        result.Message!.Params.ShouldBe(["/queue/mine?search=[redacted]", 7]);
        result.Request.Data.ShouldBe("search=[redacted]&page=2");
        result.Request.Cookies.ShouldBe("theme=dark; last=/queue/mine?status=Open&search=[redacted]");
    }

    [Fact]
    public void A_request_body_that_is_not_text_is_left_alone()
    {
        var body = new { page = 2 };
        var sentryEvent = new SentryEvent();
        sentryEvent.Request.Data = body;

        var result = new SensitiveQuerySentryProcessor().Process(sentryEvent);

        result!.Request.Data.ShouldBeSameAs(body);
    }

    [Fact]
    public void A_transaction_loses_the_search_from_its_request_body_and_cookies()
    {
        var tracer = new TransactionTracer(DisabledHub.Instance, new TransactionContext("GET /queue/{view}", "http.server", null, null, null, "", null, null, true, TransactionNameSource.Route));
        tracer.Request.Data = $"search={Needle}";
        tracer.Request.Cookies = $"last=/queue/mine?search={Needle}";

        var result = new SensitiveQuerySentryProcessor().Process(new SentryTransaction(tracer));

        result.ShouldNotBeNull();
        result.Request.Data.ShouldBe("search=[redacted]");
        result.Request.Cookies.ShouldBe("last=/queue/mine?search=[redacted]");
    }

    [Fact]
    public void A_breadcrumb_that_carries_a_search_is_replaced_by_a_masked_copy_and_any_other_is_kept_as_it_is()
    {
        var dirty = new Breadcrumb(
            $"GET /api/tickets?search={Needle}", "http",
            new Dictionary<string, string> { ["url"] = $"https://api.test/api/tickets?search={Needle}&page=1", ["http.query"] = $"?search={Needle}&page=1", ["method"] = "GET" },
            "http", BreadcrumbLevel.Info);
        var clean = new Breadcrumb("GET /api/agents/me", "http", new Dictionary<string, string> { ["url"] = "https://api.test/api/agents/me" }, "http", BreadcrumbLevel.Info);
        var bare = new Breadcrumb("a log line", "default");

        var masked = SensitiveQuerySentryProcessor.ScrubBreadcrumb(dirty, new SentryHint());

        masked.ShouldNotBeNull();
        masked.Message.ShouldBe("GET /api/tickets?search=[redacted]");
        masked.Data!["url"].ShouldBe("https://api.test/api/tickets?search=[redacted]&page=1");
        masked.Data["http.query"].ShouldBe("?search=[redacted]&page=1");
        masked.Data["method"].ShouldBe("GET");
        masked.Type.ShouldBe("http");
        masked.Category.ShouldBe("http");
        masked.Level.ShouldBe(BreadcrumbLevel.Info);
        SensitiveQuerySentryProcessor.ScrubBreadcrumb(clean, new SentryHint()).ShouldBeSameAs(clean);
        SensitiveQuerySentryProcessor.ScrubBreadcrumb(bare, new SentryHint()).ShouldBeSameAs(bare);
    }

    [Fact]
    public void The_host_registration_masks_the_search_in_an_event_and_a_breadcrumb_that_reach_the_transport_and_without_it_the_harness_sees_the_leak()
    {
        var withoutScrubbing = Send(register: false);
        var withScrubbing = Send(register: true);

        // The control: a registration that scrubs nothing delivers the needle in every place, so this harness would catch a leak.
        withoutScrubbing.Split(Needle).Length.ShouldBeGreaterThanOrEqualTo(6);
        withScrubbing.ShouldNotContain(Needle);
        withScrubbing.ShouldContain("search=[redacted]");
        withScrubbing.ShouldContain("status=Open");
    }

    private static string Send(bool register)
    {
        var transport = new CapturingTransport();
        var options = new SentryOptions
        {
            Dsn = "https://key@sentry.example.test/1",
            Transport = transport,
            AutoSessionTracking = false,
            CacheDirectoryPath = null,
        };
        if (register)
        {
            options.AddSensitiveHeaderScrubbing();
        }

        using var sdk = SentrySdk.Init(options);
        SentrySdk.AddBreadcrumb(
            $"GET /api/tickets?search={Needle}", "http", "http", new Dictionary<string, string> { ["url"] = $"https://api.test/api/tickets?search={Needle}" }, BreadcrumbLevel.Info);
        var sentryEvent = new SentryEvent { Message = "boom" };
        sentryEvent.Request.QueryString = $"?status=Open&search={Needle}";
        sentryEvent.Request.Url = $"https://admin.test/queue/mine?status=Open&search={Needle}";
        sentryEvent.Request.Headers["Referer"] = $"https://admin.test/queue/mine?status=Open&search={Needle}";
        sentryEvent.SetTag("page", $"/queue/mine?search={Needle}");
        SentrySdk.CaptureEvent(sentryEvent);
        SentrySdk.FlushAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

        return transport.Payloads.Single();
    }

    private sealed class CapturingTransport : ITransport
    {
        public List<string> Payloads { get; } = [];

        public async Task SendEnvelopeAsync(Envelope envelope, CancellationToken cancellationToken = default)
        {
            using var stream = new MemoryStream();
            await envelope.SerializeAsync(stream, null, cancellationToken);
            Payloads.Add(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        }
    }
}

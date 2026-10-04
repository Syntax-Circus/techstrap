using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Intake;
using TechStrap.Application.Security;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;
using TechStrap.Hosting.Logging;

namespace TechStrap.Api.Tests.Redaction;

public sealed class LogRedactionTests(TestPostgres postgres) : IDisposable
{
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE";   // exactly 43 base64url characters
    private static readonly string Hash = "sha256:" + new string('a', 64);
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-redaction-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }
    }

    private sealed class Sink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static (Serilog.ILogger Log, Sink Sink) NewLogger()
    {
        var sink = new Sink();
        return (new LoggerConfiguration().Enrich.With<PiiRedactionEnricher>().WriteTo.Sink(sink).CreateLogger(), sink);
    }

    private static string Everything(LogEvent e) =>
        string.Join('\n', [e.RenderMessage(), .. e.Properties.Values.Select(v => v.ToString())]);

    [Theory]
    [InlineData("ada@example.com", "[email]")]
    [InlineData("mail Ada.Lovelace+tag@sub.example.co.uk now", "mail [email] now")]
    [InlineData("josé@bücher.example", "[email]")]
    [InlineData("用户@例え.テスト", "[email]")]
    [InlineData("ada@[10.0.0.1]", "[email]")]
    [InlineData("ada@example.x", "[email]")]
    [InlineData("ada@my_host.example.com", "[email]")]
    [InlineData("\"ada lovelace\"@example.com", "[email]")]
    [InlineData("o'brien@example.com", "[email]")]
    public void Emails_in_scalar_values_are_redacted(string input, string expected)
    {
        var (log, sink) = NewLogger();
        log.Information("Value {Value}", input);
        sink.Events.Single().Properties["Value"].ShouldBeOfType<ScalarValue>().Value.ShouldBe(expected);
    }

    [Fact]
    public void Tokens_and_hashes_are_redacted_but_other_text_is_not()
    {
        var (log, sink) = NewLogger();
        var guid = Guid.NewGuid();
        log.Information("{Token} {Hash} {Id} {Number} {N32} {Long} {Short} {Count}",
            Token, Hash, guid, "ORB-42", guid.ToString("N"), Token + "x", Token[..42], 7);
        var e = sink.Events.Single();
        Value(e, "Token").ShouldBe("[token]");
        Value(e, "Hash").ShouldBe("[hash]");
        e.Properties["Id"].ShouldBeOfType<ScalarValue>().Value.ShouldBe(guid);
        Value(e, "Number").ShouldBe("ORB-42");
        Value(e, "N32").ShouldBe(guid.ToString("N"));      // 32 characters: not a token
        Value(e, "Long").ShouldBe(Token + "x");            // a 44-character run (a random long id) is not a token
        Value(e, "Short").ShouldBe(Token[..42]);
        e.Properties["Count"].ShouldBeOfType<ScalarValue>().Value.ShouldBe(7);
        static object? Value(LogEvent e, string name) => ((ScalarValue)e.Properties[name]).Value;
    }

    [Theory]
    [InlineData("tsk_" + Token, "[token]")]
    [InlineData("tsp_" + Token, "[token]")]
    [InlineData("https://help.test/t%2F" + Token, "https://help.test/t%2F[token]")]
    [InlineData("SHA256:ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD", "[hash]")]
    public void Api_keys_encoded_tokens_and_uppercase_hashes_are_redacted(string input, string expected)
    {
        var (log, sink) = NewLogger();
        log.Information("Value {Value}", input);
        sink.Events.Single().Properties["Value"].ShouldBeOfType<ScalarValue>().Value.ShouldBe(expected);
    }

    [Fact]
    public void Colliding_dictionary_keys_are_disambiguated_and_siblings_stay_redacted()
    {
        var (log, sink) = NewLogger();
        log.Information("dup {Map} {Other}",
            new Dictionary<string, int> { ["ada@example.com"] = 1, ["bob@example.com"] = 2 }, "carol@example.com");

        var e = sink.Events.Single();
        var text = Everything(e);
        text.ShouldNotContain("@example.com");
        e.Properties["Map"].ShouldBeOfType<DictionaryValue>().Elements.Count.ShouldBe(2);
        ((ScalarValue)e.Properties["Other"]).Value.ShouldBe("[email]");
    }

    [Fact]
    public void A_scalar_that_is_not_a_string_is_redacted_through_its_text()
    {
        var (log, sink) = NewLogger();
        log.Information("{Link} {Plain}", new Uri("https://help.test/t/" + Token + "?e=ada@example.com"), new Uri("https://help.test/about"));

        var e = sink.Events.Single();
        var link = e.Properties["Link"].ShouldBeOfType<ScalarValue>().Value.ShouldBeOfType<string>();
        link.ShouldNotContain(Token);
        link.ShouldNotContain("ada@example.com");
        e.Properties["Plain"].ShouldBeOfType<ScalarValue>().Value.ShouldBeOfType<Uri>();   // unchanged text keeps the original value
    }

    [Fact]
    public void A_failing_redaction_replaces_only_that_property_and_leaves_no_pii()
    {
        var sink = new Sink();
        var log = new LoggerConfiguration()
            .Enrich.With(new PiiRedactionEnricher(text => text.Contains("boom", StringComparison.Ordinal) ? throw new TimeoutException() : PiiRedactionEnricher.RedactText(text)))
            .WriteTo.Sink(sink).CreateLogger();

        log.Information("{A} {B} {C}", "boom ada@example.com", "carol@example.com", new[] { "x", "boom bob@example.com" });

        var e = sink.Events.Single();
        ((ScalarValue)e.Properties["A"]).Value.ShouldBe(PiiRedactionEnricher.FailedMarker);
        ((ScalarValue)e.Properties["B"]).Value.ShouldBe("[email]");
        e.Properties["C"].ShouldBeOfType<ScalarValue>().Value.ShouldBe(PiiRedactionEnricher.FailedMarker);
        Everything(e).ShouldNotContain("@example.com");
    }

    [Fact]
    public void Nested_and_exception_paths_carry_no_email_or_token()
    {
        var (log, sink) = NewLogger();
        var person = new { Name = "Ada", Email = "ada@example.com", Aliases = new[] { "ada@example.com" } };
        log.Information("nested {@Person} {Seq} {Dict}", person,
            new[] { "ada@example.com", "ok" }, new Dictionary<string, string> { ["ada@example.com"] = Token });
        // an exception passed as a template argument is stringified by Serilog into a scalar, message and all
        log.Warning("failed {Error}", new InvalidOperationException($"duplicate key (email)=(ada@example.com) token {Token} {Hash}"));

        foreach (var e in sink.Events)
        {
            var text = Everything(e);
            text.ShouldNotContain("ada@example.com");
            text.ShouldNotContain(Token);
            text.ShouldNotContain(Hash);
        }

        Everything(sink.Events[0]).ShouldContain("Name: \"Ada\"");     // structure shape and non-PII values survive
        Everything(sink.Events[0]).ShouldContain("ok");
    }

    [Fact]
    public void Redaction_is_idempotent()
    {
        var (log, sink) = NewLogger();
        log.Information("x {V}", "ada@example.com");
        var once = sink.Events.Single();
        var before = Everything(once);

        new PiiRedactionEnricher().Enrich(once, null!);

        Everything(once).ShouldBe(before);
        before.ShouldContain(PiiRedactionEnricher.EmailMarker);
    }

    [Fact]
    public async Task The_api_host_redacts_what_application_code_logs()
    {
        await using var factory = new ApiFactory();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RedactionProbe");

        logger.LogWarning("Probe {Email} {Token} {Hash}", "ada@example.com", Token, Hash);

        var probe = factory.LogSink.Events.Single(e => e.MessageTemplate.Text.StartsWith("Probe ", StringComparison.Ordinal));
        probe.RenderMessage().ShouldBe("Probe \"[email]\" \"[token]\" \"[hash]\"");
    }

    [Fact]
    public async Task The_worker_host_redacts_what_application_code_logs()
    {
        await using var factory = new WorkerFactory(settings: new Dictionary<string, string?> { ["ConnectionStrings:TechStrap"] = await postgres.CreateDatabaseAsync() });
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RedactionProbe");

        logger.LogWarning("Probe {Email} {Token} {Hash}", "ada@example.com", Token, Hash);

        var probe = factory.LogSink.Events.Single(e => e.MessageTemplate.Text.StartsWith("Probe ", StringComparison.Ordinal));
        probe.RenderMessage().ShouldBe("Probe \"[email]\" \"[token]\" \"[hash]\"");
    }

    private static async Task WaitForRequestEventsAsync(ApiFactory factory, params (string Path, int Count)[] expected)
    {
        static bool Matches(LogEvent e, string path) =>
            e.MessageTemplate.Text.StartsWith("HTTP {RequestMethod}", StringComparison.Ordinal)   // the request-logging summary, not a scoped event
            && e.Properties.TryGetValue("RequestPath", out var value) && value is ScalarValue { Value: string p } && p == path;

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline
            && !expected.All(x => factory.LogSink.Events.Count(e => Matches(e, x.Path)) >= x.Count))
        {
            await Task.Delay(50, Ct);
        }

        foreach (var (path, count) in expected)
        {
            factory.LogSink.Events.Count(e => Matches(e, path)).ShouldBeGreaterThanOrEqualTo(count, $"request logging for {path}");
        }
    }

    [Fact]
    public async Task The_intake_customer_and_lost_link_flow_logs_no_email_name_or_token()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test", ["Storage:Local:RootPath"] = _storage };
        await using var factory = new ApiFactory(settings: settings);
        var seed = await IntakeTestData.SeedAsync(factory.Services, Ct);
        using var client = factory.CreateClient();

        // 1. intake with a key (the same request shape SensitiveDataLeakTests uses)
        using var intake = new HttpRequestMessage(HttpMethod.Post, "/api/intake/tickets")
        {
            Content = JsonContent.Create(new SubmitTicketRequest("ada@example.com", "Ada Lovelace", "Help", "Please help", null, null)),
        };
        intake.Headers.Add(HeaderNames.ApiKey, seed.OrbitlyTrusted);
        using var submitted = await client.SendAsync(intake, Ct);
        submitted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var viewUrl = (await submitted.Content.ReadFromJsonAsync<SubmitTicketResponse>(Ct))!.ViewUrl!;
        var token = viewUrl[(viewUrl.IndexOf("/t/", StringComparison.Ordinal) + 3)..].Split('?', '#', '/')[0];

        // 2. the customer reads the ticket with the token
        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket");
        read.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
        (await client.SendAsync(read, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // 3. a lost-link request for the same address, and one for an unknown address
        foreach (var address in new[] { "ada@example.com", "nobody@example.com" })
        {
            using var lost = await client.PostAsJsonAsync("/api/customer/access-link", new RequestNewAccessLinkRequest(address), Ct);
            lost.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        // request logging runs after the response; wait until every request has produced its event so the scan cannot pass vacuously
        await WaitForRequestEventsAsync(factory, ("/api/intake/tickets", 1), ("/api/customer/ticket", 1), ("/api/customer/access-link", 2));

        string hash;
        using (var scope = factory.Services.CreateScope())
        {
            hash = scope.ServiceProvider.GetRequiredService<IAccessTokenService>().Hash(token);
        }

        foreach (var e in factory.LogSink.Events)
        {
            var text = Everything(e) + "\n" + e.Exception;
            foreach (var needle in new[] { "ada@example.com", "nobody@example.com", "Ada Lovelace", token, hash })
            {
                text.ShouldNotContain(needle, Case.Insensitive);
            }
        }
    }
}

using Serilog;
using Serilog.Events;
using TechStrap.Hosting.Logging;

namespace TechStrap.Api.Tests.Redaction;

/// <summary>
/// P09-T17 / T21: the contact page may be opened with <c>?name=...&amp;email=...</c> (a prefill from the product's own app). A name cannot be recognized by pattern, so the value of these two query
/// parameters is masked wherever a logged text carries a query string, in addition to the email and token patterns the redactor already has. The match is on the parameter's decoded name, so
/// <c>%6Eame=</c> and <c>NAME=</c> are caught, and on nothing else: a text that merely says "name=" is left alone.
/// </summary>
public sealed class PiiRedactionQueryValueTests
{
    private sealed class Sink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Theory]
    [InlineData("?name=Jane%20Doe", "?name=[redacted]")]
    [InlineData("?name=Jane+Doe&email=jane%40example.com", "?name=[redacted]&email=[redacted]")]
    [InlineData("?subject=Hi&name=Jane&page=2", "?subject=[redacted]&name=[redacted]&page=2")]
    [InlineData("?NAME=Jane&Email=a@b.example", "?NAME=[redacted]&Email=[redacted]")]
    [InlineData("?%6Eame=Jane&%65mail=x", "?%6Eame=[redacted]&%65mail=[redacted]")]
    [InlineData("?name=", "?name=[redacted]")]
    [InlineData("?name=Jane&name=Joe", "?name=[redacted]&name=[redacted]")]
    [InlineData("name=Jane", "name=[redacted]")]
    [InlineData("Request starting HTTP/1.1 GET http://localhost/p/paperplane/contact?name=Jane%20Doe&email=jane.doe%40example.com - -", "Request starting HTTP/1.1 GET http://localhost/p/paperplane/contact?name=[redacted]&email=[redacted] - -")]
    [InlineData("https://portal.test/p/x/contact?name=O%27Brien#top", "https://portal.test/p/x/contact?name=[redacted]#top")]
    [InlineData("?subject=Hi%20there", "?subject=[redacted]")]
    [InlineData("?ref=CfDJ8NaYTe_x-1&other=1", "?ref=[redacted]&other=1")]
    [InlineData("/p/x/contact/received?ref=CfDJ8NaYTe_x-1", "/p/x/contact/received?ref=[redacted]")]
    [InlineData("?q=reset%20my%20password&page=2", "?q=[redacted]&page=2")]
    [InlineData("GET /p/x/suggest?q=jane.doe+printer HTTP/1.1", "GET /p/x/suggest?q=[redacted] HTTP/1.1")]
    [InlineData("?SUBJECT=a&Ref=b&Q=c", "?SUBJECT=[redacted]&Ref=[redacted]&Q=[redacted]")]
    [InlineData("?%73ubject=a&%72ef=b&%71=c", "?%73ubject=[redacted]&%72ef=[redacted]&%71=[redacted]")]
    [InlineData("?name=O'Brien&page=2", "?name=[redacted]&page=2")]
    [InlineData("?email=a\"b@x.y#top", "?email=[redacted]#top")]
    public void The_value_of_name_and_email_in_a_query_string_is_masked_and_the_rest_is_kept(string text, string expected) =>
        PiiRedactionEnricher.RedactText(text).ShouldBe(expected);

    [Theory]
    [InlineData("a message that says name=nothing but is not a query")]
    [InlineData("?username=jane&nickname=j&surname=d&filename=f.txt&email2=x")]
    [InlineData("?subjects=1&reference=2&refs=3&query=4&sq=5&xq=6")]
    [InlineData("text/html;q=0.9")]
    [InlineData("a message that says q=1 and ref=2 and subject=3 but is not a query")]
    [InlineData("?%6Eam%65s=1")]
    [InlineData("hostname=db&email-from=x")]
    [InlineData("Name is Jane")]
    [InlineData("")]
    public void Anything_that_is_not_a_name_or_email_parameter_is_left_alone(string text) =>
        PiiRedactionEnricher.RedactText(text).ShouldBe(text);

    [Fact]
    public void A_logged_query_string_property_is_masked_before_any_sink_sees_it()
    {
        var sink = new Sink();
        using var log = new LoggerConfiguration().Enrich.With<PiiRedactionEnricher>().WriteTo.Sink(sink).CreateLogger();

        log.Information("Request starting {Path}{QueryString}", "/p/paperplane/contact", "?name=Jane%20Doe&email=jane.doe%40example.com");

        var rendered = sink.Events.Single().RenderMessage();
        rendered.ShouldBe("Request starting \"/p/paperplane/contact\"\"?name=[redacted]&email=[redacted]\"");
        rendered.ShouldNotContain("Jane");
        rendered.ShouldNotContain("jane.doe");
    }

    [Fact]
    public void A_very_long_value_is_masked_in_one_pass()
    {
        PiiRedactionEnricher.RedactText("?name=" + new string('x', 100_000)).ShouldBe("?name=[redacted]");
    }
}

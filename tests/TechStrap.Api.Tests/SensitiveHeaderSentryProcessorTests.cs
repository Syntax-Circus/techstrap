using Sentry;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests;

public sealed class SensitiveHeaderSentryProcessorTests
{
    [Fact]
    public void Credential_headers_are_removed_case_insensitively_and_others_kept()
    {
        var sentryEvent = new SentryEvent();
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["x-ticket-token"] = "t", ["X-TICKET-TOKEN"] = "t2", ["X-Api-Key"] = "k", ["authorization"] = "Bearer x", ["COOKIE"] = "c",
            ["User-Agent"] = "ua", ["Accept"] = "*/*",
        })
        {
            sentryEvent.Request.Headers[key] = value;
        }

        var result = new SensitiveHeaderSentryProcessor().Process(sentryEvent);

        result.ShouldNotBeNull();
        result.Request.Headers.Keys.Order().ShouldBe(["Accept", "User-Agent"]);
    }

    [Fact]
    public void Transaction_credential_headers_are_removed_and_others_kept()
    {
        var transaction = new SentryTransaction("GET /x", "http.server");
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["x-ticket-token"] = "t", ["X-Api-Key"] = "k", ["authorization"] = "Bearer x", ["COOKIE"] = "c", ["User-Agent"] = "ua",
        })
        {
            transaction.Request.Headers[key] = value;
        }

        var result = new SensitiveHeaderSentryProcessor().Process(transaction);

        result.ShouldNotBeNull();
        result.Request.Headers.Keys.ShouldBe(["User-Agent"]);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TechStrap.Api.Tests.Redaction;

/// <summary>The Admin handles requester data from PHASE-07 on (ticket subjects, names, emails), so its logs are redacted like the Api's and the Worker's (D-039, D-040).</summary>
public sealed class AdminHostRedactionTests
{
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE";   // exactly 43 base64url characters
    private static readonly string Hash = "sha256:" + new string('a', 64);

    [Fact]
    public async Task The_admin_host_redacts_what_application_code_logs()
    {
        await using var factory = new AdminFactory();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RedactionProbe");

        logger.LogWarning("Probe {Email} {Token} {Hash}", "ada@example.com", Token, Hash);

        var probe = factory.LogSink.Events.Single(e => e.MessageTemplate.Text.StartsWith("Probe ", StringComparison.Ordinal));
        probe.RenderMessage().ShouldBe("Probe \"[email]\" \"[token]\" \"[hash]\"");
    }

    // SyntaxCircus.Blazor.Auth logs PathAndQuery, which can carry the queue search the agent typed, URL-encoded.
    [Fact]
    public async Task The_admin_host_redacts_a_url_encoded_email_and_a_jwt_in_a_logged_path()
    {
        await using var factory = new AdminFactory();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SyntaxCircus.Blazor.Auth.ApiAuthHandler");

        logger.LogWarning("Auth probe {Path} {Bearer}", "/api/tickets?view=all&search=jane%40example.com&page=1", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhZGEifQ.c2lnbmF0dXJlX3BhcnQ");

        var probe = factory.LogSink.Events.Single(e => e.MessageTemplate.Text.StartsWith("Auth probe ", StringComparison.Ordinal)).RenderMessage();
        probe.ShouldNotContain("jane");
        probe.ShouldNotContain("example.com");
        probe.ShouldNotContain("eyJ");
        probe.ShouldContain("[email]");
        probe.ShouldContain("[token]");
    }
}

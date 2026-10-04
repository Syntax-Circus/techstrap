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
}

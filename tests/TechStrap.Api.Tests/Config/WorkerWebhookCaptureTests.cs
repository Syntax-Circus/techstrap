namespace TechStrap.Api.Tests.Config;

/// <summary>
/// The webhook capture switch at Worker start-up: a Release build (what ships) refuses to start when the setting is present, and a Debug build
/// accepts it only in Development. The key is written out here because a Release build has no code that declares it as a constant for tests to use.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class WorkerWebhookCaptureTests
{
    private const string Key = "TECHSTRAP_WEBHOOK_CAPTURE_URL";
    private const string CaptureUrl = "http://localhost:55270/capture";

    private static async Task<Exception?> StartWorkerAsync(string environment, string? captureUrl)
    {
        using var variables = new ScopedEnvironment(("DotEnv__Enabled", "false"), ("TrustedProxy__TrustedNetworks__0", null), (Key, null));
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            // The same settings WorkerFactory uses: no background loop touches a database or SMTP server.
            ["EmailOutbox:Enabled"] = "false",
            ["AutoClose:Enabled"] = "false",
            ["OutboxRetention:Enabled"] = "false",
        };
        if (captureUrl is not null)
        {
            settings[Key] = captureUrl;
        }

        return await ConfigHosts.TryStartAsync(HostKind.Worker, environment, settings);
    }

    [Fact]
    public async Task Worker_starts_without_a_capture_url()
    {
        var failure = await StartWorkerAsync("Development", captureUrl: null);

        failure.ShouldBeNull($"The Worker did not start: {failure}");
    }

#if DEBUG
    [Fact]
    public async Task Debug_worker_refuses_capture_in_Production()
    {
        var failure = await StartWorkerAsync("Production", CaptureUrl);

        failure.ShouldNotBeNull();
        failure.ToString().ShouldContain(Key);
        failure.ToString().ShouldContain("Development");
    }
#else
    [Fact]
    public async Task Release_worker_refuses_a_capture_url()
    {
        var failure = await StartWorkerAsync("Development", CaptureUrl);

        failure.ShouldNotBeNull();
        failure.ToString().ShouldContain(Key);
        failure.ToString().ShouldContain("Debug builds");
    }
#endif
}

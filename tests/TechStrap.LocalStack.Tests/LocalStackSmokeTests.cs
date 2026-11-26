using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using TechStrap.AppHost;
using TechStrap.AppHost.Authentik;
using TechStrap.Tests.Shared;

namespace TechStrap.LocalStack.Tests;

/// <summary>
/// Boots the real local Aspire stack (Docker, Postgres, Mailpit, Authentik and the applications), checks that it serves
/// and takes one ticket through to the emailed confirmation. Opt-in only, never run in CI: set
/// TECHSTRAP_LOCALSTACK_SMOKE=1. It expects the development seed data (LocalStack:SeedDevData, on by default).
///
/// The AppHost runs as a child process (<c>dotnet run</c>) rather than through DistributedApplicationTestingBuilder: the
/// testing builder randomizes container host ports, but the stack depends on fixed ports (the Authentik issuer URL and
/// the hosts' own launch profiles). Persistent containers are left running afterwards, as they are after a normal run.
/// Two copies cannot share the ports, so stop any running AppHost first.
/// </summary>
public sealed class LocalStackSmokeTests
{
    private const string EnableVariable = "TECHSTRAP_LOCALSTACK_SMOKE";
    private const string SeededProduct = "orbitly";
    private const string RequesterAddress = "smoke@techstrap.localhost";

    private static readonly string _authentik = $"http://localhost:{LocalStackPorts.Authentik}";
    private static readonly string _issuer = $"{_authentik}/application/o/{AuthentikResources.Slug}/";
    private static readonly string _api = $"https://localhost:{LocalStackPorts.ApiHttps}";
    private static readonly string _admin = $"https://localhost:{LocalStackPorts.AdminHttps}";
    private static readonly string _portal = $"https://localhost:{LocalStackPorts.PortalHttps}";
    private static readonly string _worker = $"http://localhost:{LocalStackPorts.Worker}";
    private static readonly string _mailpit = $"http://127.0.0.1:{LocalStackPorts.MailpitUi}";

    // What dies with the AppHost. Not the container ports: persistent containers outlive it.
    private static readonly int[] _processPorts =
    [
        LocalStackPorts.Dashboard, LocalStackPorts.DashboardOtlp, LocalStackPorts.ResourceService,
        LocalStackPorts.ApiHttps, LocalStackPorts.ApiHttp, LocalStackPorts.AdminHttps, LocalStackPorts.AdminHttp,
        LocalStackPorts.PortalHttps, LocalStackPorts.PortalHttp, LocalStackPorts.Worker, LocalStackPorts.WebhookCatcher,
    ];

    // One shared budget for the whole boot and the ticket, inside the 15 minute Fact timeout so Assert.Fail fires first.
    private static readonly TimeSpan _stackTimeout = TimeSpan.FromMinutes(12);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = 900_000)]
    public async Task Local_stack_boots_serves_and_emails_a_ticket_confirmation()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(EnableVariable) == "1",
            $"Opt-in: set {EnableVariable}=1 to boot the local Aspire stack.");

        // Every wait below observes this token, so they stop as soon as the Fact times out.
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();

        var repositoryRoot = RepositoryRoot.Find();
        var apiKey = ReadSeededTrustedKey();

        // Otherwise the checks below could pass against a stack this test did not start.
        var taken = ListeningAmong(_processPorts);
        taken.ShouldBeEmpty(
            $"Ports already in use: {string.Join(", ", taken)}. Another AppHost or local application appears to be "
            + $"running (the dashboard uses {LocalStackPorts.Dashboard}). Stop it and retry; nothing was started or stopped.");

        var logPath = Path.Combine(Path.GetTempPath(), $"techstrap-apphost-smoke-{Guid.NewGuid():N}.log");
        // Synchronized: the AppHost's two output streams and this method write to it.
        await using var log = TextWriter.Synchronized(new StreamWriter(
            new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        { AutoFlush = true });
        // The job object kills the whole AppHost tree if the test host itself dies; disposing it at the end does the same.
        using var job = KillOnCloseJob.TryCreate();
        if (job is null && OperatingSystem.IsWindows())
        {
            WarnNoJobObject(log, "it could not be created");
        }
        Process? appHost = null;
        var passed = false;

        try
        {
            appHost = StartAppHost(repositoryRoot, log);
            if (job is not null && !job.TryAssign(appHost))
            {
                WarnNoJobObject(log, "the AppHost could not be assigned to it");
            }

            using var http = CreateClient(allowAutoRedirect: true);
            using var noRedirect = CreateClient(allowAutoRedirect: false);
            var deadline = DateTime.UtcNow + _stackTimeout;

            await PollAsync(() => CheckStackAsync(http, noRedirect), appHost, deadline, logPath);

            // Once only, with everything up: intake, the outbox, the Worker and SMTP.
            var subject = $"Local stack smoke {Guid.NewGuid():N}";
            await SubmitTicketAsync(http, apiKey, subject, logPath);
            await PollAsync(() => CheckConfirmationEmailAsync(http, subject), appHost, deadline, logPath);
            passed = true;
        }
        finally
        {
            // The applications die with the AppHost tree; persistent containers stay.
            if (appHost is not null)
            {
                await StopAsync(appHost);
            }

            // Before the wait for the ports: this reaps whatever the tree kill missed.
            job?.Dispose();

            if (passed)
            {
                await log.DisposeAsync();
                TryDelete(logPath);
            }
        }

        // The whole tree must be gone, or the next run could not take the ports.
        await WaitForPortsToCloseAsync();
    }

    /// <summary>Runs every check once. Returns null when all pass, otherwise a one-line description of the first failure.</summary>
    private static async Task<string?> CheckStackAsync(HttpClient http, HttpClient noRedirect)
    {
        var discoveryUrl = $"{_issuer}.well-known/openid-configuration";
        using (var discovery = await http.GetAsync(discoveryUrl, Ct))
        {
            if (discovery.StatusCode != HttpStatusCode.OK)
            {
                return $"{discoveryUrl} returned {(int)discovery.StatusCode}";
            }

            var issuer = (await discovery.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("issuer").GetString();
            if (issuer != _issuer)
            {
                return $"The Authentik issuer is '{issuer}', expected '{_issuer}'";
            }
        }

        foreach (var url in (string[])[$"{_api}/health/ready", $"{_worker}/health/ready", $"{_portal}/p/{SeededProduct}"])
        {
            using var response = await http.GetAsync(url, Ct);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return $"{url} returned {(int)response.StatusCode}";
            }
        }

        // The Admin sends an anonymous visitor to its own landing page, and that page's button on to the provider.
        return await CheckRedirectAsync(noRedirect, $"{_admin}/", $"{_admin}/signin?", "/signin?")
            ?? await CheckRedirectAsync(
                noRedirect, $"{_admin}/signin/start", $"{_authentik}/application/o/authorize/?");
    }

    private static async Task<string?> CheckRedirectAsync(HttpClient noRedirect, string url, params string[] expectedPrefixes)
    {
        using var response = await noRedirect.GetAsync(url, Ct);
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        return (int)response.StatusCode is >= 300 and <= 399
            && expectedPrefixes.Any(prefix => location.StartsWith(prefix, StringComparison.Ordinal))
                ? null
                // Without the query, which carries the sign-in state.
                : $"{url} returned {(int)response.StatusCode} to '{location.Split('?')[0]}', "
                    + $"expected a redirect to {expectedPrefixes[0]}";
    }

    private static async Task SubmitTicketAsync(HttpClient http, string apiKey, string subject, string logPath)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_api}/api/intake/tickets")
        {
            Content = JsonContent.Create(new
            {
                email = RequesterAddress,
                name = "Smoke Test",
                subject,
                body = "Filed by the local stack smoke test.",
            }),
        };
        request.Headers.Add("X-Api-Key", apiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var response = await http.SendAsync(request, Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"The Api refused the ticket with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)} "
            + $"(a 401 usually means the development seed data is off: LocalStack:SeedDevData). AppHost log: {logPath}");
    }

    private static async Task<string?> CheckConfirmationEmailAsync(HttpClient http, string subject)
    {
        // Newest first.
        var inbox = await http.GetFromJsonAsync<JsonElement>($"{_mailpit}/api/v1/messages", Ct);
        return inbox.GetProperty("messages")
            .EnumerateArray()
            .Any(message => IsRequesterConfirmation(message, subject))
                ? null
                : $"No confirmation to {RequesterAddress} for the ticket '{subject}' has reached Mailpit";
    }

    // Both on one message: the subject only the confirmation has, addressed to the requester.
    private static bool IsRequesterConfirmation(JsonElement message, string subject)
        => message.GetProperty("Subject").GetString()!.EndsWith($"We've got your request: {subject}", StringComparison.Ordinal)
            && message.GetProperty("To").EnumerateArray().Any(to => string.Equals(
                to.GetProperty("Address").GetString(), RequesterAddress, StringComparison.OrdinalIgnoreCase));

    // Always runs: it needs no stack. Agents who want new-ticket alerts get mail that carries the ticket's subject too.
    [Theory]
    [InlineData("[ORB-1] We've got your request: Smoke 1", RequesterAddress, true)]
    [InlineData("[ORB-1] New ticket: Smoke 1", "riley@techstrap.localhost", false)]
    [InlineData("[ORB-1] New ticket: Smoke 1", RequesterAddress, false)]
    [InlineData("[ORB-1] We've got your request: Smoke 1", "riley@techstrap.localhost", false)]
    [InlineData("[ORB-1] We've got your request: Smoke 2", RequesterAddress, false)]
    public void Only_the_requester_confirmation_counts_as_delivered(string mailSubject, string recipient, bool expected)
    {
        // The shape of one entry of Mailpit's /api/v1/messages.
        var message = JsonSerializer.SerializeToElement(
            new { Subject = mailSubject, To = new[] { new { Name = string.Empty, Address = recipient } } });

        IsRequesterConfirmation(message, "Smoke 1").ShouldBe(expected);
    }

    // The log is deleted when the test passes, so the test output gets the line too.
    private static void WarnNoJobObject(TextWriter log, string reason)
    {
        var line = $"Warning: no kill-on-close job object ({reason}). If the test host dies, the AppHost tree may keep running.";
        log.WriteLine(line);
        TestContext.Current.TestOutputHelper?.WriteLine(line);
    }

    // Read from the source text: the test must not reference Infrastructure.
    private static string ReadSeededTrustedKey()
    {
        var source = File.ReadAllText(
            RepositoryRoot.Combine("src", "TechStrap.Infrastructure", "Seeding", "DevelopmentApiKeys.cs"));
        var match = Regex.Match(source, "\\bOrbitlyTrusted\\s*=\\s*\"(?<key>[^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1));
        match.Success.ShouldBeTrue("DevelopmentApiKeys.OrbitlyTrusted was not found in its source file.");
        return match.Groups["key"].Value;
    }

    private static async Task PollAsync(Func<Task<string?>> check, Process appHost, DateTime deadline, string logPath)
    {
        var lastFailure = "no check completed";
        while (true)
        {
            Ct.ThrowIfCancellationRequested();
            if (appHost.HasExited)
            {
                Assert.Fail(
                    $"The AppHost exited early (code {appHost.ExitCode}). Is Docker running? Last failure: {lastFailure}. "
                    + $"AppHost log: {logPath}");
            }

            string? failure;
            try
            {
                failure = await check();
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException
                or KeyNotFoundException or IOException or InvalidOperationException)
            {
                Ct.ThrowIfCancellationRequested();
                failure = $"{exception.GetType().Name}: {exception.Message}";
            }

            if (failure is null)
            {
                return;
            }

            lastFailure = failure;
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail(
                    $"Not passing within {_stackTimeout.TotalMinutes:0} minutes. Last failure: {failure}. AppHost log: {logPath}");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), Ct);
        }
    }

    private static Process StartAppHost(string repositoryRoot, TextWriter log)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repositoryRoot,
        };

        // Debug whatever the tests were built as: the AppHost refuses to start in a Release build.
        var project = Path.Combine(repositoryRoot, "tools", "TechStrap.AppHost", "TechStrap.AppHost.csproj");
        foreach (var argument in (string[])["run", "--project", project, "-c", "Debug"])
        {
            info.ArgumentList.Add(argument);
        }

        // Otherwise "dotnet run" on an AppHost hands over to "aspire run".
        info.Environment["ASPIRE_SUPPRESS_CLI_RUN_HOOK"] = "true";
        // No shared build servers: one started here would die with this tree while other builds were using it.
        info.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        info.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        info.Environment["UseSharedCompilation"] = "false";

        var process = new Process { StartInfo = info };
        void Write(string? line)
        {
            if (line is null)
            {
                return;
            }

            try
            {
                log.WriteLine(line);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        process.OutputDataReceived += (_, e) => Write(e.Data);
        process.ErrorDataReceived += (_, e) => Write(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    // Only ever the tree this test started.
    private static async Task StopAsync(Process appHost)
    {
        try
        {
            appHost.Kill(entireProcessTree: true);
            // Not the test's token: the tree must be reaped even when the test was canceled.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await appHost.WaitForExitAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception
            or NotSupportedException or AggregateException or OperationCanceledException)
        {
            // Already gone, or not killable; the job object (when available) still reaps the tree.
        }
        finally
        {
            appHost.Dispose();
        }
    }

    private static async Task WaitForPortsToCloseAsync()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);
        for (var open = ListeningAmong(_processPorts); open.Length > 0; open = ListeningAmong(_processPorts))
        {
            (DateTime.UtcNow < deadline).ShouldBeTrue(
                $"Still listening after the AppHost was killed: {string.Join(", ", open)}");
            await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        }
    }

    // The listener table, not connection attempts: Windows takes seconds to refuse each closed port.
    private static int[] ListeningAmong(int[] ports) =>
    [
        .. IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Select(endpoint => endpoint.Port)
            .Intersect(ports)
            .Order(),
    ];

    // The hosts use the local development certificate; accept it, but only for loopback addresses.
    private static HttpClient CreateClient(bool allowAutoRedirect) =>
        new(new HttpClientHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
            ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
                errors == SslPolicyErrors.None || request.RequestUri?.IsLoopback == true,
        })
        { Timeout = TimeSpan.FromSeconds(30) };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}

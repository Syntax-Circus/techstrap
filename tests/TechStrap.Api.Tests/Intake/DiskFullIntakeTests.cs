using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Support;

namespace TechStrap.Api.Tests.Intake;

/// <summary>
/// Review SR-11 (no storage quota): when the disk fills part-way through a submission the caller gets a clean ProblemDetails, no ticket is left half written and no file
/// is orphaned. Production is the environment that matters (Development answers with the developer exception page), so the class runs in the non-parallel
/// <see cref="ProcessEnvironmentCollection"/> with the trusted network set the way a deployed host has it.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class DiskFullIntakeTests(TestPostgres postgres) : IDisposable
{
    private const string TrustedNetworkVariable = "TrustedProxy__TrustedNetworks__0";

    private static readonly byte[] _png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-diskfull-intake-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task A_full_disk_on_the_second_file_is_a_clean_problem_response_with_no_ticket_and_no_file()
    {
        Environment.SetEnvironmentVariable(TrustedNetworkVariable, "10.20.30.0/24");
        try
        {
            var database = await ApiTestDatabase.CreateAsync(postgres);
            var settings = new Dictionary<string, string?>(database.Settings)
            {
                ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
                ["Storage:Local:RootPath"] = _storage,
            };
            await using var factory = new ApiFactory(environment: "Production", settings: settings, configureServices: services => FailingStorageProvider.Register(services, failOnStoreCall: 2));
            await IntakeTestData.SeedAsync(factory.Services, TestContext.Current.CancellationToken);
            using var client = factory.CreateClient();
            using var form = new MultipartFormDataContent
            {
                { new StringContent("ada@example.com"), "email" },
                { new StringContent("Ada"), "name" },
                { new StringContent("Help"), "subject" },
                { new StringContent("Please help"), "body" },
            };
            foreach (var name in new[] { "first.png", "second.png" })
            {
                var file = new ByteArrayContent(_png);
                file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
                form.Add(file, "attachments", name);
            }

            using var response = await client.PostAsync("/api/public/products/orbitly/tickets", form, TestContext.Current.CancellationToken);

            var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, text);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            using var problem = JsonDocument.Parse(text);
            problem.RootElement.GetProperty("status").GetInt32().ShouldBe(500);
            problem.RootElement.GetProperty("type").GetString().ShouldBe("internal-error");
            problem.RootElement.GetProperty("detail").GetString().ShouldBe("An unexpected error occurred."); // the fixed generic text, never the exception message
            text.ShouldNotContain("No space left");
            text.ShouldNotContain("IOException");
            text.ShouldNotContain(" at ");
            (await database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(0);
            (await database.ScalarAsync<long>("SELECT count(*) FROM messages")).ShouldBe(0);
            (await database.ScalarAsync<long>("SELECT count(*) FROM attachments")).ShouldBe(0);
            (Directory.Exists(_storage) ? Directory.GetFiles(_storage, "*", SearchOption.AllDirectories) : []).ShouldBeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedNetworkVariable, null);
        }
    }
}

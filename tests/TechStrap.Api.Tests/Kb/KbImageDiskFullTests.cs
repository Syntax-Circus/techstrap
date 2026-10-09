using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Support;

namespace TechStrap.Api.Tests.Kb;

/// <summary>
/// Review SR-11 for the KB image upload: a full disk is a clean ProblemDetails and leaves no file behind. The upload handler checks the signed-in agent in the database
/// before it stores anything, so the test runs against a real database with the agent provisioned; without one the request fails earlier and never reaches storage.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class KbImageDiskFullTests(TestPostgres postgres) : IDisposable
{
    private const string TrustedNetworkVariable = "TrustedProxy__TrustedNetworks__0";

    private static readonly byte[] _png = [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }, .. new byte[40]];

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-diskfull-kb-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task A_full_disk_on_a_kb_image_upload_is_a_clean_problem_response_and_leaves_no_file()
    {
        Environment.SetEnvironmentVariable(TrustedNetworkVariable, "10.20.30.0/24");
        try
        {
            var database = await ApiTestDatabase.CreateAsync(postgres);
            var settings = new Dictionary<string, string?>(database.Settings) { ["Storage:Local:RootPath"] = _storage };
            await using var factory = new ApiFactory(environment: "Production", settings: settings, configureServices: services => FailingStorageProvider.Register(services, failOnStoreCall: 1));
            using var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
            (await agent.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode(); // provisions the agent the upload handler requires
            var file = new ByteArrayContent(_png);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            using var form = new MultipartFormDataContent { { file, "file", "photo.png" } };

            using var response = await agent.PostAsync("/api/kb/images", form, TestContext.Current.CancellationToken);

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
            Directory.Exists(Path.Combine(_storage, "kb-images")).ShouldBeTrue(); // the failed store really reached the provider and wrote its partial object
            (Directory.Exists(_storage) ? Directory.GetFiles(_storage, "*", SearchOption.AllDirectories) : []).ShouldBeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedNetworkVariable, null);
        }
    }
}

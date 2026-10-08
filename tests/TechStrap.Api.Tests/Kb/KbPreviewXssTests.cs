using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Kb;
using TechStrap.Tests.Shared;

namespace TechStrap.Api.Tests.Kb;

/// <summary>P12-T05: the shared XSS corpus through the agent preview endpoint (Markdown in, sanitised HTML out).</summary>
public sealed class KbPreviewXssTests(TestPostgres postgres)
{
    [Fact(Timeout = 120_000)]
    public async Task Every_corpus_vector_renders_to_no_active_content_in_the_preview()
    {
        var ct = Xunit.TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?>(database.Settings));
        using var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        (await agent.GetAsync("/api/agents/me", ct)).EnsureSuccessStatusCode();

        var failures = new List<string>();
        for (var index = 0; index < XssCorpus.Vectors.Count; index++)
        {
            var vector = XssCorpus.Vectors[index];
            using var response = await agent.PostAsJsonAsync("/api/kb/preview", new KbPreviewRequest(vector), ct);
            if (!response.IsSuccessStatusCode)
            {
                failures.Add($"vector-{index + 1:00}: HTTP {(int)response.StatusCode} for {vector}");
                continue;
            }

            var html = (await response.Content.ReadFromJsonAsync<KbPreviewResponse>(ct))!.Html;
            if (!XssAssertions.ContainsNoActiveContent(html))
            {
                failures.Add($"vector-{index + 1:00}: {vector} -> {html}");
            }
        }

        failures.ShouldBeEmpty(string.Join("\n", failures));
    }
}

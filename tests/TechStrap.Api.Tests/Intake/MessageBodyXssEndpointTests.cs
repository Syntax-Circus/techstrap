using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;
using TechStrap.Tests.Shared;

namespace TechStrap.Api.Tests.Intake;

/// <summary>P12-T05: a hostile ticket body is inert in the agent view and in the customer view (the stored and served message BodyHtml).</summary>
public sealed class MessageBodyXssEndpointTests(TestPostgres postgres)
{
    private const string TokenMarker = "/t/";

    private static readonly string[] _bodies =
    [
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<svg onload=alert(1)>",
        "<a href=\"javascript:alert(1)\">x</a>",
        "<iframe srcdoc=\"<script>alert(1)</script>\"></iframe>",
    ];

    [Fact(Timeout = 120_000)]
    public async Task A_hostile_body_is_inert_in_the_agent_and_the_customer_views()
    {
        var ct = Xunit.TestContext.Current.CancellationToken;
        var storage = Path.Combine(Path.GetTempPath(), "techstrap-xss-" + Guid.NewGuid().ToString("N"));
        try
        {
            var database = await ApiTestDatabase.CreateAsync(postgres);
            var settings = new Dictionary<string, string?>(database.Settings)
            {
                ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
                ["Storage:Local:RootPath"] = storage,
            };
            await using var factory = new ApiFactory(settings: settings);
            var seed = await IntakeTestData.SeedAsync(factory.Services, ct);
            using var anonymous = factory.CreateClient();
            using var agent = factory.CreateClient().Bearer(TestJwt.Token("sam", [TestJwt.AgentGroup], email: "sam@example.com", name: "Sam"));
            (await agent.GetAsync("/api/agents/me", ct)).EnsureSuccessStatusCode();

            var failures = new List<string>();
            var index = 0;
            foreach (var body in _bodies)
            {
                index++;
                using var submit = new HttpRequestMessage(HttpMethod.Post, "/api/intake/tickets")
                {
                    Content = JsonContent.Create(new SubmitTicketRequest($"ada{index}@example.com", "Ada", "Hostile", body, null, null)),
                };
                submit.Headers.Add(HeaderNames.ApiKey, seed.OrbitlyTrusted);
                using var created = await anonymous.SendAsync(submit, ct);
                created.StatusCode.ShouldBe(HttpStatusCode.Created);
                var submitted = (await created.Content.ReadFromJsonAsync<SubmitTicketResponse>(ct))!;

                var detail = (await agent.GetFromJsonAsync<TicketDetailDto>($"/api/tickets/{submitted.TicketNumber}", ct))!;
                detail.Messages.ShouldNotBeEmpty();
                foreach (var message in detail.Messages)
                {
                    Check(failures, "agent", body, message.BodyHtml);
                    PinOutput(failures, "agent", body, message.BodyHtml);
                }

                var token = submitted.ViewUrl![(submitted.ViewUrl!.IndexOf(TokenMarker, StringComparison.Ordinal) + TokenMarker.Length)..];
                using var view = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket");
                view.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
                using var viewed = await anonymous.SendAsync(view, ct);
                viewed.StatusCode.ShouldBe(HttpStatusCode.OK);
                var customer = (await viewed.Content.ReadFromJsonAsync<CustomerTicketDto>(ct))!;
                customer.Messages.ShouldNotBeEmpty();
                foreach (var message in customer.Messages)
                {
                    Check(failures, "customer", body, message.BodyHtml);
                    PinOutput(failures, "customer", body, message.BodyHtml);
                }
            }

            failures.ShouldBeEmpty(string.Join("\n", failures));
        }
        finally
        {
            if (Directory.Exists(storage))
            {
                Directory.Delete(storage, true);
            }
        }
    }

    // What the sanitiser actually leaves of each vector (the message pipeline shows raw HTML as encoded text; the body is never empty).
    private static readonly Dictionary<string, string> _expected = new()
    {
        ["<script>alert(1)</script>"] = "<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>",
        ["<img src=x onerror=alert(1)>"] = "<p>&lt;img src=x onerror=alert(1)&gt;</p>",
        ["<svg onload=alert(1)>"] = "<p>&lt;svg onload=alert(1)&gt;</p>",
        ["<a href=\"javascript:alert(1)\">x</a>"] = "<p>&lt;a href=\"javascript:alert(1)\"&gt;x&lt;/a&gt;</p>",
        ["<iframe srcdoc=\"<script>alert(1)</script>\"></iframe>"] = "<p>&lt;iframe srcdoc=\"&lt;script&gt;alert(1)&lt;/script&gt;\"&gt;&lt;/iframe&gt;</p>",
    };

    private static void PinOutput(List<string> failures, string view, string body, string html)
    {
        html.ShouldNotBeNullOrWhiteSpace();
        if (html.Trim() != _expected[body])
        {
            failures.Add($"{view}: {body} -> unexpected output {html}");
        }
    }

    private static void Check(List<string> failures, string view, string body, string html)
    {
        if (!XssAssertions.ContainsNoActiveContent(html))
        {
            failures.Add($"{view}: {body} -> {html}");
        }
    }
}

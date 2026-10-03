using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests.Auth;

/// <summary>JWT validation and the group policies (D-004, D-029), against a migrated test database.</summary>
public sealed class AgentAuthTests(TestPostgres postgres)
{
    private async Task<ApiFactory> FactoryAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        return new ApiFactory(
            settings: new Dictionary<string, string?>
            {
                ["ConnectionStrings:TechStrap"] = database,
                [ApiStartupTasks.MigrateOnStartupKey] = "true",
            },
            configureServices: services => services.AddControllers().AddApplicationPart(typeof(AuthProbeController).Assembly));
    }

    private static async Task<HttpStatusCode> GetAsync(ApiFactory factory, string path, string? token)
    {
        using var client = factory.CreateClient();
        if (token is not null)
        {
            client.Bearer(token);
        }

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task A_request_without_a_token_is_401()
    {
        await using var factory = await FactoryAsync();

        (await GetAsync(factory, "/__test/agent", null)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_with_a_bad_signature_wrong_audience_wrong_issuer_or_expiry_is_401()
    {
        await using var factory = await FactoryAsync();
        var otherKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-different-key-that-is-long-enough-0123456789"));

        (await GetAsync(factory, "/__test/agent", TestJwt.Token("s1", [TestJwt.AgentGroup], signingKey: otherKey))).ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(factory, "/__test/agent", TestJwt.Token("s1", [TestJwt.AgentGroup], audience: "someone-else"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(factory, "/__test/agent", TestJwt.Token("s1", [TestJwt.AgentGroup], issuer: "https://evil.test/"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(factory, "/__test/agent", TestJwt.Token("s1", [TestJwt.AgentGroup], expires: DateTime.UtcNow.AddMinutes(-1)))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_valid_token_without_an_agent_or_admin_group_is_403()
    {
        await using var factory = await FactoryAsync();

        (await GetAsync(factory, "/__test/agent", TestJwt.Token("s1", ["someone-else"]))).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_agent_group_reaches_agent_routes_but_not_admin_routes()
    {
        await using var factory = await FactoryAsync();
        var token = TestJwt.Token("s1", [TestJwt.AgentGroup]);

        (await GetAsync(factory, "/__test/agent", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync(factory, "/__test/admin", token)).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_admin_group_alone_reaches_agent_and_admin_routes()
    {
        await using var factory = await FactoryAsync();
        var token = TestJwt.Token("s1", [TestJwt.AdminGroup]);

        (await GetAsync(factory, "/__test/agent", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync(factory, "/__test/admin", token)).ShouldBe(HttpStatusCode.OK);
    }
}

using System.Net;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

public sealed class ReferenceDataClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static AgentListItemDto Agent(int n) => new(Guid.NewGuid(), $"Agent {n}", $"Agent {n} (Orbitly)", null, null, null, null);

    [Fact]
    public async Task Me_returns_the_agent_dto_from_the_me_route()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.WithTestAgents();

        var result = await api.Get<IAgentsClient>().GetMeAsync(Ct);

        result.Value.Role.ShouldBe(AgentRoles.Admin);
        result.Value.Email.ShouldBe(AdminTestPrincipal.Admin.Email);
        api.Stub.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/agents/me");
    }

    [Theory]
    [InlineData("agent-access-required")]
    [InlineData("agent-inactive")]
    [InlineData("agent-email-required")]
    [InlineData("agent-identity-invalid")]
    public async Task Me_maps_each_403_to_a_forbidden_result_carrying_the_api_code(string code)
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnProblem(HttpMethod.Get, "/api/agents/me", HttpStatusCode.Forbidden, code, "No access.");

        var result = await api.Get<IAgentsClient>().GetMeAsync(Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(code, "No access.", ResultErrorKind.Forbidden));
    }

    [Fact]
    public async Task ListAll_fetches_pages_of_100_until_every_agent_is_read()
    {
        await using var api = await ApiHarness.CreateAsync();
        var everyone = Enumerable.Range(1, 230).Select(Agent).ToList();
        api.Stub.On(HttpMethod.Get, "/api/agents", request =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.Query);
            var page = int.Parse(query["page"]!);
            query["pageSize"].ShouldBe("100");
            return StubApiHandler.JsonResponse(HttpStatusCode.OK, new PagedResponse<AgentListItemDto>([.. everyone.Skip((page - 1) * 100).Take(100)], page, 100, everyone.Count));
        });

        var result = await api.Get<IAgentsClient>().ListAllAsync(Ct);

        result.Value.Count.ShouldBe(230);
        api.Stub.Requests.Select(r => r.Query).ShouldBe(["?page=1&pageSize=100", "?page=2&pageSize=100", "?page=3&pageSize=100"]);
    }

    [Fact]
    public async Task ListAll_stops_on_an_empty_page_and_returns_the_first_failure()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnJson(HttpMethod.Get, "/api/agents", new PagedResponse<AgentListItemDto>([], 1, 100, 500));
        (await api.Get<IAgentsClient>().ListAllAsync(Ct)).Value.ShouldBeEmpty();

        api.Stub.OnProblem(HttpMethod.Get, "/api/agents", HttpStatusCode.Forbidden, "agent-inactive", "Deactivated.");
        (await api.Get<IAgentsClient>().ListAllAsync(Ct)).Errors[0].Code.ShouldBe("agent-inactive");
    }

    [Fact]
    public async Task Products_and_tags_are_read_as_lists()
    {
        await using var api = await ApiHarness.CreateAsync();
        var product = new ProductDto(Guid.NewGuid(), "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#2563EB", "#FFFFFF", "#1E40AF", null, null), 3);
        api.Stub.OnJson(HttpMethod.Get, "/api/products", new[] { product });
        api.Stub.OnJson(HttpMethod.Get, "/api/tags", new[] { new TagDto(Guid.NewGuid(), "bug", "Bug", "#DC2626") });

        (await api.Get<IProductsClient>().ListAsync(Ct)).Value.Single().NumberPrefix.ShouldBe("ORB");
        (await api.Get<ITagsClient>().ListAsync(Ct)).Value.Single().Slug.ShouldBe("bug");
    }
}

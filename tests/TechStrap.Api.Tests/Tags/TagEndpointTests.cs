using TechStrap.Domain.Tickets;
using TechStrap.Domain.Requesters;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Agents;
using TechStrap.Application.Persistence;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Tags;

namespace TechStrap.Api.Tests.Tags;

public sealed class TagEndpointTests(TestPostgres postgres)
{
    private static async Task SignInAsync(HttpClient client) =>
        (await client.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    private static async Task<TagDto> CreateAsync(HttpClient admin, string slug)
    {
        using var response = await admin.PostAsJsonAsync("/api/tags", new CreateTagRequest(slug, slug, "#DC2626"), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TagDto>(TestContext.Current.CancellationToken))!;
    }

    private async Task<(ApiFactory Factory, HttpClient Admin, HttpClient Agent)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        await SignInAsync(admin);
        await SignInAsync(agent);
        return (factory, admin, agent);
    }

    [Fact]
    public async Task An_admin_creates_updates_lists_and_deletes_an_unused_tag()
    {
        var (factory, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;

        var created = await CreateAsync(admin, "bug");
        (await admin.GetFromJsonAsync<List<TagDto>>("/api/tags", TestContext.Current.CancellationToken))!.Select(tag => tag.Id).ShouldContain(created.Id);

        using var update = await admin.PutAsJsonAsync($"/api/tags/{created.Id}", new UpdateTagRequest("Defect", "#2563eb"), TestContext.Current.CancellationToken);
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await update.Content.ReadFromJsonAsync<TagDto>(TestContext.Current.CancellationToken))!.ShouldSatisfyAllConditions(
            tag => tag.Name.ShouldBe("Defect"),
            tag => tag.Colour.ShouldBe("#2563EB"));

        using var delete = await admin.DeleteAsync($"/api/tags/{created.Id}", TestContext.Current.CancellationToken);
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<List<TagDto>>("/api/tags", TestContext.Current.CancellationToken))!.Select(tag => tag.Id).ShouldNotContain(created.Id);
    }

    [Fact]
    public async Task A_duplicate_slug_is_409()
    {
        var (factory, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        await CreateAsync(admin, "bug");

        using var response = await admin.PostAsJsonAsync("/api/tags", new CreateTagRequest("bug", "Other", "#2563EB"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_bad_colour_is_400_with_a_colour_field_error()
    {
        var (factory, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;

        using var response = await admin.PostAsJsonAsync("/api/tags", new CreateTagRequest("bug", "Bug", "red"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("colour");
    }

    [Fact]
    public async Task An_agent_can_list_tags_but_cannot_create_update_or_delete()
    {
        var (factory, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var created = await CreateAsync(admin, "bug");

        using var list = await agent.GetAsync("/api/tags", TestContext.Current.CancellationToken);
        using var create = await agent.PostAsJsonAsync("/api/tags", new CreateTagRequest("other", "Other", "#2563EB"), TestContext.Current.CancellationToken);
        using var update = await agent.PutAsJsonAsync($"/api/tags/{created.Id}", new UpdateTagRequest("Defect", "#2563EB"), TestContext.Current.CancellationToken);
        using var delete = await agent.DeleteAsync($"/api/tags/{created.Id}", TestContext.Current.CancellationToken);

        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        delete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Puts a tag on a ticket through the DI container (the Api has no ticket endpoints yet): a requester, a numbered ticket and its tag.</summary>
    private static async Task AttachTagToNewTicketAsync(ApiFactory factory, Guid agentId, Guid productId, Guid tagId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var clock = services.GetRequiredService<TimeProvider>();
        await using var work = await services.GetRequiredService<IUnitOfWork>().BeginAsync(cancellationToken);
        var requester = Requester.Create("ann@example.com", "Ann", null, clock).Value;
        services.GetRequiredService<IRequesterRepository>().Add(requester);
        var number = (await services.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(productId, cancellationToken)).Value;
        var ticket = Ticket.Create(number, productId, requester.Id, "Cannot sign in", TicketChannel.Web, null, false, clock).Value;
        ticket.AddCustomerReply(requester.Id, "<p>I cannot sign in</p>", clock).IsSuccess.ShouldBeTrue();
        ticket.AddTag(tagId, Actor.ForAgent(agentId), clock).IsSuccess.ShouldBeTrue();
        services.GetRequiredService<ITicketRepository>().Add(ticket);
        (await work.CommitAsync(cancellationToken)).IsSuccess.ShouldBeTrue();
    }

    private static async Task<(Guid ProductId, Guid AgentId)> ProductAndAdminAsync(HttpClient admin)
    {
        using var response = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest("acme", "Acme", "ACME", null), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var product = (await response.Content.ReadFromJsonAsync<ProductDto>(TestContext.Current.CancellationToken))!;
        var me = (await admin.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken))!;
        return (product.Id, me.Id);
    }

    [Fact]
    public async Task Deleting_a_tag_on_a_ticket_is_409_tag_in_use_without_force_and_204_with_force()
    {
        var (factory, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var (productId, agentId) = await ProductAndAdminAsync(admin);
        var tag = await CreateAsync(admin, "bug");
        await AttachTagToNewTicketAsync(factory, agentId, productId, tag.Id);

        using var refused = await admin.DeleteAsync($"/api/tags/{tag.Id}", TestContext.Current.CancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("tag-in-use");
        (await admin.GetFromJsonAsync<List<TagDto>>("/api/tags", TestContext.Current.CancellationToken))!.Select(t => t.Id).ShouldContain(tag.Id);

        using var forced = await admin.DeleteAsync($"/api/tags/{tag.Id}?force=true", TestContext.Current.CancellationToken);

        forced.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<List<TagDto>>("/api/tags", TestContext.Current.CancellationToken))!.Select(t => t.Id).ShouldNotContain(tag.Id);
    }
}

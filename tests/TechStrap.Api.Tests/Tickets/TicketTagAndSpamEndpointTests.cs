using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Tickets;

public sealed class TicketTagAndSpamEndpointTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, TicketSeed Seed)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        return (factory, await TicketTestData.SeedAsync(factory, Ct));
    }

    private static async Task<IReadOnlyList<string>> NumbersAsync(HttpClient client, string view) =>
        (await client.GetFromJsonAsync<PagedResponse<TicketSummaryDto>>($"/api/tickets?view={view}", Ct))!.Items.Select(row => row.Number).ToList();

    [Fact]
    public async Task A_spammed_ticket_leaves_the_normal_views_and_not_spam_brings_it_back_with_its_status()
    {
        var (factory, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var ticket = seed.Tickets[1];
        var number = ticket.Number.ToString();
        (await NumbersAsync(sam, "open")).ShouldContain(number);

        var version = await TicketTestData.VersionAsync(sam, ticket.Id);
        using var marked = await sam.PutAsJsonAsync($"/api/tickets/{ticket.Id}/spam", new MarkTicketSpamRequest(true, version), Ct);
        marked.StatusCode.ShouldBe(HttpStatusCode.OK);
        var spammed = (await marked.Content.ReadFromJsonAsync<TicketStateDto>(Ct))!;
        spammed.IsSpam.ShouldBeTrue();

        (await NumbersAsync(sam, "open")).ShouldNotContain(number);
        (await NumbersAsync(sam, "spam")).ShouldContain(number);

        using var cleared = await sam.PutAsJsonAsync($"/api/tickets/{ticket.Id}/spam", new MarkTicketSpamRequest(false, spammed.RowVersion), Ct);
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        var restored = (await cleared.Content.ReadFromJsonAsync<TicketStateDto>(Ct))!;
        restored.IsSpam.ShouldBeFalse();
        restored.Status.ShouldBe(spammed.Status);
        (await NumbersAsync(sam, "open")).ShouldContain(number);
    }

    [Fact]
    public async Task Tag_add_and_remove_round_trip_with_row_versions()
    {
        var (factory, seed) = await StartAsync();
        await using var _f = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var ticket = seed.Tickets[1];

        var version = await TicketTestData.VersionAsync(sam, ticket.Id);
        using var added = await sam.PostAsJsonAsync($"/api/tickets/{ticket.Id}/tags", new AddTicketTagRequest(seed.Billing.Id, version), Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterAdd = (await added.Content.ReadFromJsonAsync<TicketStateDto>(Ct))!;
        afterAdd.TagIds.ShouldBe([seed.Billing.Id]);
        afterAdd.RowVersion.ShouldNotBe(version);

        using var removed = await sam.DeleteAsync($"/api/tickets/{ticket.Id}/tags/{seed.Billing.Id}?rowVersion={afterAdd.RowVersion}", Ct);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterRemove = (await removed.Content.ReadFromJsonAsync<TicketStateDto>(Ct))!;
        afterRemove.TagIds.ShouldBeEmpty();

        using var noVersion = await sam.DeleteAsync($"/api/tickets/{ticket.Id}/tags/{seed.Billing.Id}", Ct);
        noVersion.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_agent_without_admin_may_mark_spam()
    {
        var (factory, seed) = await StartAsync();
        await using var _f = factory;
        using var kim = TicketTestData.AgentClient(factory, "kim");
        var ticket = seed.Tickets[0];

        var version = await TicketTestData.VersionAsync(kim, ticket.Id);
        using var response = await kim.PutAsJsonAsync($"/api/tickets/{ticket.Id}/spam", new MarkTicketSpamRequest(true, version), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

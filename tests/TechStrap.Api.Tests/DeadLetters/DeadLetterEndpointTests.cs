using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.DeadLetters;
using TechStrap.Contracts.Paging;
using TechStrap.Domain.Outbox;

namespace TechStrap.Api.Tests.DeadLetters;

public sealed class DeadLetterEndpointTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string SecretLink = "https://portal.test/t/SECRETLINK";

    private sealed record Started(ApiFactory Factory, ApiTestDatabase Database, HttpClient Admin) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Admin.Dispose();
            await Factory.DisposeAsync();
        }
    }

    private async Task<Started> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var factory = new ApiFactory(settings: database.Settings);
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        (await admin.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        return new Started(factory, database, admin);
    }

    /// <summary>Queues one row through the real outbox, then forces the stored state (dead-lettered when asked) and the creation time.</summary>
    private static async Task<Guid> SeedAsync(Started started, string address, int minutesAgo, bool deadLettered = true)
    {
        Guid id;
        await using (var scope = started.Factory.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            var item = EmailOutboxItem.Enqueue(
                "ticket-confirmation", address, "{\"portalLink\":\"" + SecretLink + "\"}", null, null, provider.GetRequiredService<TimeProvider>()).Value;
            await using var unitOfWork = await provider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
            provider.GetRequiredService<IEmailOutbox>().Enqueue(item);
            (await unitOfWork.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
            id = item.Id;
        }

        if (deadLettered)
        {
            await started.Database.ExecuteAsync(
                $"UPDATE email_outbox SET status = 'DeadLettered', attempts = 5, last_error = 'smtp-permanent', created_at = now() - interval '{minutesAgo} minutes' WHERE id = '{id}'");
        }

        return id;
    }

    private static async Task<PagedResponse<DeadLetterDto>> ListAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<PagedResponse<DeadLetterDto>>("/api/dead-letters" + query, Ct))!;

    [Fact]
    public async Task The_list_shows_masked_recipients_newest_first_and_never_the_payload()
    {
        await using var started = await StartAsync();
        var older = await SeedAsync(started, "ann@example.com", minutesAgo: 10);
        var newer = await SeedAsync(started, "bob@example.com", minutesAgo: 1);

        using var response = await started.Admin.GetAsync("/api/dead-letters", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = System.Text.Json.JsonSerializer.Deserialize<PagedResponse<DeadLetterDto>>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        page.Items.Select(i => i.Id).ShouldBe([newer, older]);
        page.Items.Select(i => i.Recipient).ShouldBe(["b***@example.com", "a***@example.com"]);
        page.Items.ShouldAllBe(i => i.Attempts == 5 && i.LastError == "smtp-permanent" && i.Kind == "ticket-confirmation");
        body.ShouldNotContain("payload", Case.Insensitive);
        body.ShouldNotContain("SECRETLINK");
        body.ShouldNotContain("portalLink");
        body.Replace("***@example.com", string.Empty, StringComparison.Ordinal).ShouldNotContain("@example.com");
    }

    [Fact]
    public async Task Retry_returns_204_and_the_row_leaves_the_list()
    {
        await using var started = await StartAsync();
        var id = await SeedAsync(started, "ann@example.com", minutesAgo: 1);

        using var response = await started.Admin.PostAsync($"/api/dead-letters/{id}/retry", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(started.Admin)).TotalCount.ShouldBe(0);
        (await started.Database.ScalarAsync<long>($"SELECT count(*) FROM email_outbox WHERE id = '{id}' AND status = 'Pending' AND attempts = 0")).ShouldBe(1);
        (await started.Database.ScalarAsync<long>("SELECT count(*) FROM admin_events WHERE type = 'DeadLetterRetried'")).ShouldBe(1);
    }

    [Fact]
    public async Task Discard_returns_204_and_the_row_leaves_the_list()
    {
        await using var started = await StartAsync();
        var id = await SeedAsync(started, "ann@example.com", minutesAgo: 1);

        using var response = await started.Admin.DeleteAsync($"/api/dead-letters/{id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(started.Admin)).TotalCount.ShouldBe(0);
        (await started.Database.ScalarAsync<long>($"SELECT count(*) FROM email_outbox WHERE id = '{id}' AND status = 'Discarded'")).ShouldBe(1);
        (await started.Database.ScalarAsync<long>("SELECT count(*) FROM admin_events WHERE type = 'DeadLetterDiscarded'")).ShouldBe(1);
    }

    [Fact]
    public async Task Retry_or_discard_of_a_pending_row_is_409_and_an_unknown_id_is_404()
    {
        await using var started = await StartAsync();
        var pending = await SeedAsync(started, "ann@example.com", minutesAgo: 1, deadLettered: false);

        using var retry = await started.Admin.PostAsync($"/api/dead-letters/{pending}/retry", null, Ct);
        using var discard = await started.Admin.DeleteAsync($"/api/dead-letters/{pending}", Ct);
        using var unknownRetry = await started.Admin.PostAsync($"/api/dead-letters/{Guid.NewGuid()}/retry", null, Ct);
        using var unknownDiscard = await started.Admin.DeleteAsync($"/api/dead-letters/{Guid.NewGuid()}", Ct);

        retry.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await retry.Content.ReadAsStringAsync(Ct)).ShouldContain("outbox-not-dead-lettered");
        discard.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await discard.Content.ReadAsStringAsync(Ct)).ShouldContain("outbox-not-dead-lettered");
        unknownRetry.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknownRetry.Content.ReadAsStringAsync(Ct)).ShouldContain("outbox-not-found");
        unknownDiscard.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await started.Database.ScalarAsync<long>($"SELECT count(*) FROM email_outbox WHERE id = '{pending}' AND status = 'Pending'")).ShouldBe(1);
    }

    [Fact]
    public async Task An_agent_who_is_not_an_admin_gets_403_on_all_three_routes()
    {
        await using var started = await StartAsync();
        var id = await SeedAsync(started, "ann@example.com", minutesAgo: 1);
        using var sam = TicketTestData.AgentClient(started.Factory, "sam");
        using var anonymous = started.Factory.CreateClient();

        using var list = await sam.GetAsync("/api/dead-letters", Ct);
        using var retry = await sam.PostAsync($"/api/dead-letters/{id}/retry", null, Ct);
        using var discard = await sam.DeleteAsync($"/api/dead-letters/{id}", Ct);

        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        retry.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        discard.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync("/api/dead-letters", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync($"/api/dead-letters/{id}/retry", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/api/dead-letters/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await started.Database.ScalarAsync<long>($"SELECT count(*) FROM email_outbox WHERE id = '{id}' AND status = 'DeadLettered'")).ShouldBe(1);
    }

    [Fact]
    public async Task Paging_is_honoured_and_clamped()
    {
        await using var started = await StartAsync();
        await SeedAsync(started, "ann@example.com", minutesAgo: 10);
        await SeedAsync(started, "bob@example.com", minutesAgo: 1);

        var one = await ListAsync(started.Admin, "?pageSize=1");
        var clamped = await ListAsync(started.Admin, "?pageSize=100000");

        one.Items.Count.ShouldBe(1);
        one.TotalCount.ShouldBe(2);
        clamped.PageSize.ShouldBe(Paging.MaxPageSize);
        clamped.Items.Count.ShouldBe(2);
    }
}

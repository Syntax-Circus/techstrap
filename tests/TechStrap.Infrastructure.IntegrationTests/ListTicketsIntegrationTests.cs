using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// The list handler on the real repositories over 500 bulk tickets plus two hand-made ones. The GIN index plan is checked in
/// <see cref="TicketQueryPlanTests"/> (PHASE-03), not here.
/// </summary>
public sealed class ListTicketsIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const int BulkCount = 500;

    // Bulk row g: spam when g % 50 == 1 (10 rows), Solved when g % 20 == 0 (25 rows), assigned to the agent when g % 5 == 0,
    // tagged when g % 7 == 0, moved to Orbitly when g % 3 == 0. BULK-42 is renamed ORB-42 so the number search has a target.
    private static bool IsSpam(int g) => g % 50 == 1;

    private static bool IsSolved(int g) => g % 20 == 0;

    private static bool IsAssigned(int g) => g % 5 == 0;

    private static string NumberOf(int g) => g == 42 ? "ORB-42" : $"BULK-{g}";

    private sealed class StubClaims(string subject) : ICurrentAgentClaims
    {
        public AgentClaims? Current { get; } = new(subject, "Sam W.", "sam@example.com", AgentRole.Agent);
    }

    private sealed record Seeded(TicketScenario Scenario, Guid TagId);

    private async Task<Seeded> SeedAsync(PersistenceTestHost host)
    {
        var scenario = await TicketScenario.CreateAsync(host);
        var tag = Tag.Create("billing", "Billing", "#DC2626", host.Clock).Value;
        await host.CommitAsync(sp => { sp.GetRequiredService<ITagRepository>().Add(tag); return Task.CompletedTask; });

        // Two real tickets (New, unassigned): "zebra" is only in the body of the first and only in the subject of the second.
        await scenario.CreateTicketAsync("Alpha question", firstMessage: "<p>a zebra crossing</p>");
        await scenario.CreateTicketAsync("Zebra herd", firstMessage: "<p>nothing here</p>");

        await TicketBulkSeed.RunAsync(scenario, Database.ConnectionString, BulkCount, host.Clock.GetUtcNow(), distinctTimes: true, Ct);
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tickets SET assignee_id = @agent WHERE number LIKE 'BULK-%' AND substring(number from 6)::int % 5 = 0;
            UPDATE tickets SET product_id = @orbitly WHERE number LIKE 'BULK-%' AND substring(number from 6)::int % 3 = 0;
            INSERT INTO ticket_tags (ticket_id, tag_id) SELECT id, @tag FROM tickets WHERE number LIKE 'BULK-%' AND substring(number from 6)::int % 7 = 0;
            UPDATE tickets SET number = 'ORB-42' WHERE number = 'BULK-42';
            """;
        command.Parameters.AddWithValue("agent", scenario.Agent.Id);
        command.Parameters.AddWithValue("orbitly", scenario.Orbitly.Id);
        command.Parameters.AddWithValue("tag", tag.Id);
        await command.ExecuteNonQueryAsync(Ct);
        return new Seeded(scenario, tag.Id);
    }

    private static async Task<PagedResponse<TicketSummaryDto>> ListAsync(PersistenceTestHost host, ListTicketsRequest request)
    {
        var result = await TryListAsync(host, request);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static async Task<SyntaxCircus.Common.Result<PagedResponse<TicketSummaryDto>>> TryListAsync(PersistenceTestHost host, ListTicketsRequest request)
    {
        await using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        var handler = new ListTicketsRequestHandler(
            new StubClaims("oidc|sam"), sp.GetRequiredService<IAgentRepository>(), sp.GetRequiredService<ITicketRepository>(),
            sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<ITagRepository>());
        return await handler.HandleAsync(request, Ct);
    }

    private static ListTicketsRequest Request(string? view = null, string? search = null, int page = 1, int pageSize = 100) =>
        new(view, null, null, null, null, null, null, search, page, pageSize);

    private static async Task<List<TicketSummaryDto>> WalkAsync(PersistenceTestHost host, string? view)
    {
        var rows = new List<TicketSummaryDto>();
        for (var page = 1; ; page++)
        {
            var response = await ListAsync(host, Request(view, page: page));
            rows.AddRange(response.Items);
            if (response.Items.Count == 0 || rows.Count >= response.TotalCount)
            {
                return rows;
            }
        }
    }

    [Fact]
    public async Task Each_view_holds_exactly_its_tickets_and_spam_only_appears_in_the_spam_view()
    {
        await using var host = new PersistenceTestHost(Database);
        await SeedAsync(host);
        var bulk = Enumerable.Range(1, BulkCount).ToList();
        var extras = new[] { "ACME-1", "ACME-2" };

        string[] Expected(Func<int, bool> bulkFilter, bool includeExtras) =>
            [.. bulk.Where(bulkFilter).Select(NumberOf), .. includeExtras ? extras : []];

        var expected = new Dictionary<string, string[]>
        {
            ["Unassigned"] = Expected(g => !IsSpam(g) && !IsSolved(g) && !IsAssigned(g), true),
            ["Mine"] = Expected(g => !IsSpam(g) && !IsSolved(g) && IsAssigned(g), false),
            ["Open"] = Expected(g => !IsSpam(g) && !IsSolved(g), true),
            ["Pending"] = [],
            ["All"] = Expected(g => !IsSpam(g), true),
            ["Spam"] = Expected(IsSpam, false),
        };

        foreach (var (view, numbers) in expected)
        {
            var rows = await WalkAsync(host, view);
            rows.Select(row => row.Number).Order().ShouldBe(numbers.Order(), $"view {view}");
            rows.Any(row => row.IsSpam).ShouldBe(view == "Spam");
            rows.All(row => row.IsSpam || view != "Spam").ShouldBeTrue();
        }

        (await WalkAsync(host, null)).Count.ShouldBe(expected["All"].Length);
    }

    [Fact]
    public async Task Search_matches_subject_body_and_exact_number()
    {
        await using var host = new PersistenceTestHost(Database);
        await SeedAsync(host);

        var zebra = await ListAsync(host, Request(search: "zebra"));
        var number = await ListAsync(host, Request(search: "BULK-77"));

        zebra.Items.Select(row => row.Number).ShouldBe(["ACME-2", "ACME-1"]);
        number.Items.First().Number.ShouldBe("BULK-77");
        (await ListAsync(host, Request(search: "nonexistentword"))).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Paging_is_stable_and_complete()
    {
        await using var host = new PersistenceTestHost(Database);
        await SeedAsync(host);
        var rows = new List<TicketSummaryDto>();
        int total;

        var first = await ListAsync(host, Request("All", pageSize: 37));
        total = first.TotalCount;
        rows.AddRange(first.Items);
        for (var page = 2; rows.Count < total; page++)
        {
            var next = await ListAsync(host, Request("All", page: page, pageSize: 37));
            next.Items.ShouldNotBeEmpty();
            rows.AddRange(next.Items);
        }

        rows.Count.ShouldBe(total);
        rows.Select(row => row.Id).Distinct().Count().ShouldBe(total);
        total.ShouldBe(Enumerable.Range(1, BulkCount).Count(g => !IsSpam(g)) + 2);
        (await ListAsync(host, Request("All", page: (total / 37) + 2, pageSize: 37))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rows_carry_product_assignee_and_tag_names()
    {
        await using var host = new PersistenceTestHost(Database);
        var seeded = await SeedAsync(host);

        var row = (await ListAsync(host, Request(search: "BULK-35"))).Items.First();

        row.ShouldSatisfyAllConditions(
            r => r.Number.ShouldBe("BULK-35"),
            r => r.AssigneeName.ShouldBe("Sam W."),
            r => r.Tags.ShouldHaveSingleItem().Name.ShouldBe("Billing"),
            r => r.ProductId.ShouldBe(seeded.Scenario.Acme.Id),
            r => r.ProductName.ShouldBe("Acme"));
    }

    [Fact]
    public async Task Hostile_and_oversized_search_text_is_safe_and_number_search_is_exact()
    {
        await using var host = new PersistenceTestHost(Database);
        await SeedAsync(host);

        foreach (var text in new[] { new string('a', 10_000), "'; drop table tickets; --", "&|!:*()<>" })
        {
            var result = await TryListAsync(host, Request(search: text));
            result.IsSuccess.ShouldBeTrue(text.Length > 50 ? "oversized text" : text);
        }

        var exact = await TryListAsync(host, Request(search: "ORB-42"));

        exact.IsSuccess.ShouldBeTrue();
        exact.Value.Items.First().Number.ShouldBe("ORB-42");
        (await ListAsync(host, Request())).TotalCount.ShouldBeGreaterThan(0);
    }
}

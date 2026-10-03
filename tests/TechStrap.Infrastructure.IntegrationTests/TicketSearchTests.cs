using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>Ticket full-text search (D-011, D-027): subject, message bodies and number, ranked with the subject above the body.</summary>
public sealed class TicketSearchTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<List<string>> SearchAsync(PersistenceTestHost host, string text, TicketView view = TicketView.All)
    {
        var page = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(view, SearchText: text, PageSize: 50), Ct));
        return [.. page.Items.Select(i => i.Subject)];
    }

    [Fact]
    public async Task A_ticket_is_found_by_a_word_in_its_subject()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync("Billing question", firstMessage: "<p>hello</p>");
        await scenario.CreateTicketAsync("Login broken", firstMessage: "<p>hello</p>");

        (await SearchAsync(host, "billing")).ShouldBe(["Billing question"]);
    }

    [Fact]
    public async Task A_ticket_is_found_by_a_word_in_a_message_body_public_or_internal()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync("Hello", firstMessage: "<p>my invoice is wrong</p>");
        await scenario.CreateTicketAsync("Other", firstMessage: "<p>nothing relevant</p>");
        await scenario.UpdateAsync(ticket.Id, t => t.AddInternalNote(scenario.Agent.Id, "refund approved by finance", host.Clock));

        (await SearchAsync(host, "invoice")).ShouldBe(["Hello"]);
        (await SearchAsync(host, "refund")).ShouldBe(["Hello"]);
    }

    [Fact]
    public async Task A_ticket_is_found_by_its_number_in_any_case_and_that_match_comes_first()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync("First", firstMessage: "<p>hello</p>");
        await scenario.CreateTicketAsync("Second", firstMessage: "<p>hello</p>");

        (await SearchAsync(host, "ACME-2")).ShouldBe(["Second"]);
        (await SearchAsync(host, "acme-1")).ShouldBe(["First"]);
        (await SearchAsync(host, "ACME-99")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_stems_words_so_billed_finds_billing()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync("Billing problem", firstMessage: "<p>hello</p>");

        (await SearchAsync(host, "billed")).ShouldBe(["Billing problem"]);
    }

    [Fact]
    public async Task A_subject_match_outranks_a_body_match_even_when_the_body_match_is_newer()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync("Billing problem", firstMessage: "<p>please help</p>");
        await scenario.CreateTicketAsync("Something else", firstMessage: "<p>I have a billing problem</p>");

        (await SearchAsync(host, "billing")).ShouldBe(["Billing problem", "Something else"]);
    }

    [Fact]
    public async Task A_ticket_matching_in_both_subject_and_body_ranks_above_one_matching_in_only_the_subject()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync("Billing subject only", firstMessage: "<p>please help</p>");
        await scenario.CreateTicketAsync("Billing both", firstMessage: "<p>my billing is wrong</p>");
        await scenario.CreateTicketAsync("Body only", firstMessage: "<p>billing again</p>");

        (await SearchAsync(host, "billing")).ShouldBe(["Billing both", "Billing subject only", "Body only"]);
    }

    [Fact]
    public async Task Web_search_syntax_supports_phrases_and_exclusions()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync("Billing refund", firstMessage: "<p>x</p>");
        await scenario.CreateTicketAsync("Billing invoice", firstMessage: "<p>x</p>");
        await scenario.CreateTicketAsync("Dark mode request", firstMessage: "<p>x</p>");

        (await SearchAsync(host, "billing -refund")).ShouldBe(["Billing invoice"]);
        (await SearchAsync(host, "\"dark mode\"")).ShouldBe(["Dark mode request"]);
    }

    [Fact]
    public async Task Html_tags_in_a_sanitized_body_are_not_searchable_but_the_text_is()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync("Tagged", firstMessage: "<p class=\"secretclass\">visible words</p>");

        (await SearchAsync(host, "secretclass")).ShouldBeEmpty();
        (await SearchAsync(host, "visible")).ShouldBe(["Tagged"]);
    }

    [Fact]
    public async Task Search_respects_the_view_so_spam_appears_only_in_the_spam_view()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync("Cheap pills", firstMessage: "<p>x</p>", change: t => t.MarkSpam(true, scenario.AgentActor, host.Clock));
        await scenario.CreateTicketAsync("Cheap hosting", firstMessage: "<p>x</p>");

        (await SearchAsync(host, "cheap")).ShouldBe(["Cheap hosting"]);
        (await SearchAsync(host, "cheap", TicketView.Spam)).ShouldBe(["Cheap pills"]);
    }

    [Fact]
    public async Task Blank_search_text_means_no_search_and_no_match_gives_an_empty_page()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync("One", firstMessage: "<p>x</p>");
        await scenario.CreateTicketAsync("Two", firstMessage: "<p>x</p>");

        (await SearchAsync(host, "   ")).Count.ShouldBe(2);
        (await SearchAsync(host, "zzzzqqqq")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_pages_and_reports_the_total_of_matches()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        for (var i = 0; i < 5; i++)
        {
            await scenario.CreateTicketAsync($"Printer {i}", firstMessage: "<p>x</p>");
        }

        await scenario.CreateTicketAsync("Unrelated", firstMessage: "<p>x</p>");
        var page = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>()
            .ListAsync(new TicketQuery(TicketView.All, SearchText: "printer", Page: 2, PageSize: 2), Ct));

        page.TotalCount.ShouldBe(5);
        page.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Search_combines_with_a_product_filter()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync("Billing acme", firstMessage: "<p>x</p>");
        await scenario.CreateTicketAsync("Billing orbitly", product: scenario.Orbitly, firstMessage: "<p>x</p>");

        var page = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>()
            .ListAsync(new TicketQuery(TicketView.All, ProductId: scenario.Orbitly.Id, SearchText: "billing"), Ct));

        page.Items.ShouldHaveSingleItem().Subject.ShouldBe("Billing orbitly");
    }
}

namespace TechStrap.Architecture.Tests;

public sealed class EntryPointCatalogParserTests
{
    private const string Head = "| Entry point/use case | Named handler | Application dependencies | Infrastructure implementations | Outcome mapping | Tests | Decision |\n| --- | --- | --- | --- | --- | --- | --- |\n";

    private static string Section(string number, params string[] rows) =>
        $"### {number} Something\n\n{Head}{string.Join("\n", rows)}\n\nNot entry points: `Foo`.\n";

    [Fact]
    public void An_http_row_with_a_policy_note_parses_to_the_route_and_handler()
    {
        var entries = EntryPointCatalog.FromDocument(Section("7.1", "| `GET /api/products/{id}` (Agent) | `GetProductRequestHandler` | `IProductRepository` | EF repos | 200 | H, C | none |"));

        entries.ShouldBe([new EntryPoint("http", "GET api/products/{id}", "GetProductRequestHandler")]);
    }

    [Fact]
    public void A_route_constraint_and_a_query_string_are_dropped()
    {
        var entries = EntryPointCatalog.FromDocument(Section("7.4",
            "| `GET /api/public/kb/{productKey}/search?q=&category=` (anonymous) | `SearchHandler` | x | y | z | H | none |",
            "| `PUT /api/x/{id:guid}` (Admin) | `PutHandler` | x | y | z | H | none |"));

        entries.Select(entry => entry.Key).ShouldBe(["GET api/public/kb/{productKey}/search", "PUT api/x/{id}"]);
    }

    [Fact]
    public void The_multi_method_hub_row_yields_one_entry_per_method_with_the_same_handler()
    {
        var entries = EntryPointCatalog.FromDocument(Section("7.5", "| SignalR `TicketHub.JoinTicket` / `LeaveTicket` / `SetComposing` (Agent JWT) | `UpdateTicketPresenceHandler` | `IAgentRepository` | x | y | H | D-007 |"));

        entries.ShouldBe(
        [
            new EntryPoint("hub", "TicketHub.JoinTicket", "UpdateTicketPresenceHandler"),
            new EntryPoint("hub", "TicketHub.LeaveTicket", "UpdateTicketPresenceHandler"),
            new EntryPoint("hub", "TicketHub.SetComposing", "UpdateTicketPresenceHandler"),
        ]);
    }

    [Fact]
    public void A_loop_row_yields_the_worker_class()
    {
        var entries = EntryPointCatalog.FromDocument(Section("7.2", "| Worker outbox loop (`EmailOutboxWorker` hosted service, resolves the scoped handler) | `DrainEmailOutboxHandler` (plain Task) | x | y | z | H, W | D-010 |"));

        entries.ShouldBe([new EntryPoint("loop", "EmailOutboxWorker", "DrainEmailOutboxHandler")]);
    }

    [Fact]
    public void The_listener_row_names_the_hosted_class_not_the_notify_channel()
    {
        var entries = EntryPointCatalog.FromDocument(Section("7.5", "| Postgres `NOTIFY techstrap_ticket_changes` to API hosted listener `TicketChangeListener` | `RelayTicketChangeHandler` (listener constructor-injected) | x | y | z | H | D-018 |"));

        entries.ShouldBe([new EntryPoint("loop", "TicketChangeListener", "RelayTicketChangeHandler")]);
    }

    [Fact]
    public void A_row_with_no_entry_point_yields_nothing()
    {
        var entries = EntryPointCatalog.FromDocument(Section("7.3", "| Alerts (new ticket) | no entry point: queued by the handlers above through `ITicketNotificationPlanner` | n/a | n/a | n/a | covered | D-010 |"));

        entries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("7.6")]
    [InlineData("8.1")]
    public void A_table_outside_sections_7_1_to_7_5_is_ignored(string number)
    {
        var entries = EntryPointCatalog.FromDocument(Section(number, "| `GET /api/health` (anonymous) | `HealthHandler` | x | y | z | H | none |"));

        entries.ShouldBeEmpty();
    }

    [Fact]
    public void A_route_whose_segment_names_differ_from_the_code_is_reported_both_ways()
    {
        var doc = EntryPointCatalog.FromDocument(Section("7.2", "| `POST /api/public/products/{key}/tickets` (anonymous) | `SubmitTicketRequestHandler` | x | y | z | H | none |"));
        IReadOnlyList<EntryPoint> code = [new EntryPoint("http", "POST api/public/products/{productKey}/tickets", "SubmitTicketRequestHandler")];

        var diff = EntryPointCatalog.Diff(doc, code);

        diff.InDocNotInCode.ShouldBe(["http POST api/public/products/{key}/tickets"]);
        diff.InCodeNotInDoc.ShouldBe(["http POST api/public/products/{productKey}/tickets"]);
        diff.HandlerMismatches.ShouldBeEmpty();
    }

    [Fact]
    public void A_different_handler_for_the_same_route_is_a_mismatch()
    {
        IReadOnlyList<EntryPoint> doc = [new EntryPoint("http", "GET api/tags", "ListTagsRequestHandler")];
        IReadOnlyList<EntryPoint> code = [new EntryPoint("http", "GET api/tags", "ListAllTagsRequestHandler")];

        var diff = EntryPointCatalog.Diff(doc, code);

        diff.InDocNotInCode.ShouldBeEmpty();
        diff.InCodeNotInDoc.ShouldBeEmpty();
        diff.HandlerMismatches.ShouldHaveSingleItem().ShouldContain("ListAllTagsRequestHandler");
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Intake;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;
using TechStrap.Infrastructure.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The planner against real Postgres: outbox rows and tokens are staged in the caller's unit of work and nothing else.</summary>
public sealed class TicketNotificationPlannerTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed record Seed(Guid ProductId, Guid OtherProductId, Guid RequesterId, Guid TicketId, Guid SamId, Guid AlexId, Guid InactiveId);

    private PersistenceTestHost NewHost(string? adminUrl = "https://admin.test") =>
        new(Database, configure: services =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [PortalLinkOptions.PublicUrlKey] = "https://help.test",
                    [AdminLinkOptions.PublicUrlKey] = adminUrl,
                    ["Storage:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "techstrap-planner-" + Guid.NewGuid().ToString("N")),
                })
                .Build();
            services.AddLogging();
            services.AddTechStrapIntake(configuration);
            services.AddTechStrapTicketOperations(configuration);
        });

    private static async Task<Seed> SeedAsync(PersistenceTestHost host, bool eraseRequester = false)
    {
        Guid productId = default, otherId = default, requesterId = default, ticketId = default, samId = default, alexId = default, inactiveId = default;
        (await host.CommitAsync(sp =>
        {
            var products = sp.GetRequiredService<IProductRepository>();
            var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
            var other = Product.Create("nimbus", "Nimbus", "NIM", null, host.Clock).Value;
            products.Add(product);
            products.Add(other);
            var requester = Requester.Create("pat@example.com", "Pat", null, host.Clock).Value;
            if (eraseRequester)
            {
                requester.Erase(host.Clock);
            }

            sp.GetRequiredService<IRequesterRepository>().Add(requester);
            var agents = sp.GetRequiredService<IAgentRepository>();
            var sam = Agent.Create("sub-sam", "Sam Taylor", "sam.taylor@techstrap.test", AgentRole.Agent, host.Clock).Value;
            var alex = Agent.Create("sub-alex", "Alex Doe", "alex.doe@techstrap.test", AgentRole.Agent, host.Clock).Value;
            var gone = Agent.Create("sub-gone", "Gone Person", "gone@techstrap.test", AgentRole.Agent, host.Clock).Value;
            gone.SetActive(false);
            agents.Add(sam);
            agents.Add(alex);
            agents.Add(gone);
            var ticket = Ticket.Create(TicketNumber.Create("ORB", 1).Value, product.Id, requester.Id, "Cannot log in", TicketChannel.Web, null, false, host.Clock).Value;
            sp.GetRequiredService<ITicketRepository>().Add(ticket);
            (productId, otherId, requesterId, ticketId, samId, alexId, inactiveId) = (product.Id, other.Id, requester.Id, ticket.Id, sam.Id, alex.Id, gone.Id);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        return new Seed(productId, otherId, requesterId, ticketId, samId, alexId, inactiveId);
    }

    private async Task<long> ScalarAsync(string sql) => Convert.ToInt64(await ExecuteScalarAsync(sql));

    private async Task<string> TextAsync(string sql) => (string)(await ExecuteScalarAsync(sql))!;

    private async Task<object?> ExecuteScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(Ct);
    }

    private static async Task PlanAsync(IServiceProvider sp, Seed seed, Func<ITicketNotificationPlanner, Ticket, Agent, Agent, Task> plan)
    {
        var ticket = (await sp.GetRequiredService<ITicketRepository>().GetByIdAsync(seed.TicketId, Ct))!;
        var agents = sp.GetRequiredService<IAgentRepository>();
        var sam = (await agents.GetByIdAsync(seed.SamId, Ct))!;
        var alex = (await agents.GetByIdAsync(seed.AlexId, Ct))!;
        await plan(sp.GetRequiredService<ITicketNotificationPlanner>(), ticket, sam, alex);
    }

    private static Message AgentMessage(PersistenceTestHost host, Seed seed, Agent author) =>
        Message.Create(seed.TicketId, AuthorType.Agent, author.Id, MessageVisibility.Public, "<p>Try again.</p>", host.Clock).Value;

    [Fact]
    public async Task A_reply_queues_one_branded_email_to_the_requester_with_a_fresh_link_and_the_public_name()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);
        var messageId = Guid.Empty;

        (await host.CommitAsync(sp => PlanAsync(sp, seed, (planner, ticket, sam, _) =>
        {
            var message = AgentMessage(host, seed, sam);
            messageId = message.Id;
            return planner.PlanAgentReplyAsync(ticket, message, sam, false, Ct);
        }))).IsSuccess.ShouldBeTrue();

        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(1);
        (await TextAsync("SELECT kind FROM email_outbox")).ShouldBe("agent-reply");
        (await TextAsync("SELECT to_address FROM email_outbox")).ShouldBe("pat@example.com");
        (await TextAsync("SELECT product_id::text FROM email_outbox")).ShouldBe(seed.ProductId.ToString());
        var payload = await TextAsync("SELECT payload::text FROM email_outbox");
        payload.ShouldContain("https://help.test/t/");
        payload.ShouldContain("Sam from Orbitly Support");
        payload.ShouldContain(messageId.ToString());
        payload.ShouldNotContain("sam.taylor");
        payload.ShouldNotContain("Taylor");
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(1);
    }

    [Fact]
    public async Task The_outbox_row_and_token_roll_back_with_the_caller()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);

        await using (var scope = host.CreateScope())
        await using (await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct))
        {
            await PlanAsync(scope.ServiceProvider, seed, (planner, ticket, sam, _) => planner.PlanAgentReplyAsync(ticket, AgentMessage(host, seed, sam), sam, false, Ct));

            // Positive control: the planner really staged both rows before the scope is dropped.
            var tracker = scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().ChangeTracker;
            tracker.Entries<TicketAccessTokenRecord>().Count(e => e.State == EntityState.Added).ShouldBe(1);
            tracker.Entries<EmailOutboxRecord>().Count(e => e.State == EntityState.Added).ShouldBe(1);
        }

        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(0);
    }

    [Fact]
    public async Task An_erased_or_missing_requester_gets_no_email_and_no_token()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host, eraseRequester: true);

        (await host.CommitAsync(sp => PlanAsync(sp, seed, async (planner, ticket, sam, _) =>
        {
            await planner.PlanAgentReplyAsync(ticket, AgentMessage(host, seed, sam), sam, false, Ct);
            await planner.PlanSolvedAsync(ticket, Ct);
        }))).IsSuccess.ShouldBeTrue();

        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(0);

        // Missing: a ticket whose requester id points nowhere (never saved, only planned against).
        (await host.CommitAsync(async sp =>
        {
            var orphan = Ticket.Create(TicketNumber.Create("ORB", 2).Value, seed.ProductId, Guid.NewGuid(), "Orphan", TicketChannel.Web, null, false, host.Clock).Value;
            await sp.GetRequiredService<ITicketNotificationPlanner>().PlanSolvedAsync(orphan, Ct);
        })).IsSuccess.ShouldBeTrue();
        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(0);
    }

    [Fact]
    public async Task A_spam_ticket_gets_no_customer_email_and_no_token_but_still_alerts_the_assignee()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);

        (await host.CommitAsync(sp => PlanAsync(sp, seed, async (planner, ticket, sam, alex) =>
        {
            ticket.MarkSpam(true, Actor.ForAgent(sam.Id), host.Clock).IsSuccess.ShouldBeTrue();
            await planner.PlanAgentReplyAsync(ticket, AgentMessage(host, seed, sam), sam, false, Ct);
            await planner.PlanAgentReplyAsync(ticket, AgentMessage(host, seed, sam), sam, true, Ct);
            await planner.PlanSolvedAsync(ticket, Ct);
            await planner.PlanAssignedAsync(ticket, alex, sam, Ct);
        }))).IsSuccess.ShouldBeTrue();

        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(1);
        (await TextAsync("SELECT kind FROM email_outbox")).ShouldBe("ticket-assigned");
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(0);
    }

    [Fact]
    public async Task A_solved_notice_carries_the_reopen_window()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);

        (await host.CommitAsync(sp => PlanAsync(sp, seed, (planner, ticket, _, _) => planner.PlanSolvedAsync(ticket, Ct)))).IsSuccess.ShouldBeTrue();

        (await TextAsync("SELECT kind FROM email_outbox")).ShouldBe("ticket-solved");
        var payload = await TextAsync("SELECT payload::text FROM email_outbox");
        payload.ShouldContain($"\"reopenDays\": {TicketNotices.ReopenDays}");
        payload.ShouldContain("https://help.test/t/");
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(1);
    }

    [Fact]
    public async Task Assignment_emails_the_assignee_with_the_admin_link_but_never_on_self_assignment_or_to_an_inactive_agent()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);

        (await host.CommitAsync(sp => PlanAsync(sp, seed, async (planner, ticket, sam, alex) =>
        {
            await planner.PlanAssignedAsync(ticket, alex, sam, Ct);
            await planner.PlanAssignedAsync(ticket, sam, sam, Ct);
            var gone = (await sp.GetRequiredService<IAgentRepository>().GetByIdAsync(seed.InactiveId, Ct))!;
            await planner.PlanAssignedAsync(ticket, gone, sam, Ct);
        }))).IsSuccess.ShouldBeTrue();

        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(1);
        (await TextAsync("SELECT kind FROM email_outbox")).ShouldBe("ticket-assigned");
        (await TextAsync("SELECT to_address FROM email_outbox")).ShouldBe("alex.doe@techstrap.test");
        var payload = await TextAsync("SELECT payload::text FROM email_outbox");
        payload.ShouldContain("https://admin.test/tickets/ORB-1");
        payload.ShouldContain("Sam Taylor");
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(0);
    }

    [Fact]
    public async Task After_a_product_move_the_row_carries_the_new_product()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);

        (await host.CommitAsync(sp => PlanAsync(sp, seed, async (planner, ticket, sam, _) =>
        {
            ticket.MoveToProduct(seed.OtherProductId, Actor.ForAgent(sam.Id), host.Clock).IsSuccess.ShouldBeTrue();
            await planner.PlanAgentReplyAsync(ticket, AgentMessage(host, seed, sam), sam, true, Ct);
        }))).IsSuccess.ShouldBeTrue();

        (await TextAsync("SELECT product_id::text FROM email_outbox")).ShouldBe(seed.OtherProductId.ToString());
        (await TextAsync("SELECT payload::text FROM email_outbox")).ShouldContain("Sam from Nimbus Support");
    }

    [Fact]
    public async Task A_failed_enqueue_stages_neither_a_token_nor_a_row_and_the_callers_action_still_commits()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);
        // Stored data the outbox's address check rejects (the domain cannot create it, so write it directly).
        (await ExecuteScalarAsync("UPDATE requesters SET email = 'not-an-address' WHERE id = '" + seed.RequesterId + "'; SELECT 1")).ShouldNotBeNull();

        var result = await host.CommitAsync(sp => PlanAsync(sp, seed, async (planner, ticket, sam, _) =>
        {
            ticket.MoveToProduct(seed.OtherProductId, Actor.ForAgent(sam.Id), host.Clock).IsSuccess.ShouldBeTrue();
            sp.GetRequiredService<ITicketRepository>().Update(ticket);
            await planner.PlanAgentReplyAsync(ticket, AgentMessage(host, seed, sam), sam, false, Ct);
            await planner.PlanSolvedAsync(ticket, Ct);
        }));

        result.IsSuccess.ShouldBeTrue();
        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(0);
        (await TextAsync("SELECT product_id::text FROM tickets")).ShouldBe(seed.OtherProductId.ToString());
    }

    [Fact]
    public async Task A_missing_product_skips_the_customer_notice_and_the_assignment_alert()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);

        (await host.CommitAsync(async sp =>
        {
            var agents = sp.GetRequiredService<IAgentRepository>();
            var sam = (await agents.GetByIdAsync(seed.SamId, Ct))!;
            var alex = (await agents.GetByIdAsync(seed.AlexId, Ct))!;
            // Never saved: only planned against, so its product does not exist.
            var ticket = Ticket.Create(TicketNumber.Create("ORB", 2).Value, Guid.NewGuid(), seed.RequesterId, "Lost", TicketChannel.Web, null, false, host.Clock).Value;
            var planner = sp.GetRequiredService<ITicketNotificationPlanner>();
            await planner.PlanSolvedAsync(ticket, Ct);
            await planner.PlanAgentReplyAsync(ticket, AgentMessage(host, seed, sam), sam, false, Ct);
            await planner.PlanAssignedAsync(ticket, alex, sam, Ct);
        })).IsSuccess.ShouldBeTrue();

        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(0);
    }

    [Fact]
    public async Task An_absent_admin_url_leaves_the_admin_link_null()
    {
        await using var host = NewHost(adminUrl: null);
        var seed = await SeedAsync(host);

        (await host.CommitAsync(sp => PlanAsync(sp, seed, (planner, ticket, sam, alex) => planner.PlanAssignedAsync(ticket, alex, sam, Ct)))).IsSuccess.ShouldBeTrue();

        (await TextAsync("SELECT payload::text FROM email_outbox")).ShouldContain("\"adminLink\": null");
    }

    [Theory]
    [InlineData("ftp://admin.test")]
    [InlineData("/admin")]
    [InlineData("admin.test")]
    [InlineData("https://admin.test/?x=1")]
    [InlineData("https://admin.test/#top")]
    [InlineData("https://admin.test/?")]
    [InlineData("https://admin.test/#")]
    public async Task An_invalid_admin_url_fails_options_validation(string url)
    {
        await using var host = NewHost(adminUrl: url);
        await using var scope = host.CreateScope();

        Should.Throw<OptionsValidationException>(() => scope.ServiceProvider.GetRequiredService<IOptions<AdminLinkOptions>>().Value);
    }

    [Fact]
    public void An_admin_url_with_a_path_or_trailing_slash_is_accepted()
    {
        AdminLinkOptions.IsValidBase("https://admin.test/app/").ShouldBeTrue();
        AdminLinkOptions.IsValidBase("   ").ShouldBeTrue();
        AdminLinkOptions.IsValidBase("https://admin.test/?").ShouldBeFalse();
        AdminLinkOptions.IsValidBase("https://admin.test/#").ShouldBeFalse();
        new AdminLinkOptions { PublicUrl = "https://admin.test/app/" }.TicketLink("ORB-1").ShouldBe("https://admin.test/app/tickets/ORB-1");
    }
}

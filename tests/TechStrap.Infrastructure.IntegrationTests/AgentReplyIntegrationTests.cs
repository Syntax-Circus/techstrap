using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Attachments;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Intake;
using TechStrap.Infrastructure.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The real reply handler and planner against real Postgres and real local storage.</summary>
public sealed class AgentReplyIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres), IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "techstrap-reply-" + Guid.NewGuid().ToString("N"));

    private sealed record Seed(Guid TicketId, Guid ArticleId);

    private sealed class StubAgentClaims : ICurrentAgentClaims
    {
        public AgentClaims? Current { get; } = new("sub-sam", "Sam Taylor", "sam.taylor@techstrap.test", AgentRole.Agent);
    }

    private sealed class ThrowingOutbox(string root) : IEmailOutbox
    {
        public void Enqueue(EmailOutboxItem item)
        {
            FilesSeen = Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length : 0;
            throw new InvalidOperationException("outbox unavailable");
        }

        public static int FilesSeen { get; private set; }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PersistenceTestHost NewHost(Action<IServiceCollection>? extra = null) =>
        new(Database, configure: services =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [PortalLinkOptions.PublicUrlKey] = "https://help.test",
                    ["Storage:Local:RootPath"] = _root,
                })
                .Build();
            services.AddLogging();
            services.AddTechStrapIntake(configuration);
            services.AddTechStrapTicketOperations(configuration);
            services.AddSingleton<ICurrentAgentClaims, StubAgentClaims>();
            services.AddScoped<IAddAgentReplyRequestHandler, AddAgentReplyRequestHandler>();
            extra?.Invoke(services);
        });

    private static async Task<Seed> SeedAsync(PersistenceTestHost host, bool eraseRequester = false)
    {
        Guid ticketId = default, articleId = default;
        (await host.CommitAsync(sp =>
        {
            var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
            sp.GetRequiredService<IProductRepository>().Add(product);
            var requester = Requester.Create("pat@example.com", "Pat", null, host.Clock).Value;
            if (eraseRequester)
            {
                requester.Erase(host.Clock);
            }

            sp.GetRequiredService<IRequesterRepository>().Add(requester);
            var sam = Agent.Create("sub-sam", "Sam Taylor", "sam.taylor@techstrap.test", AgentRole.Agent, host.Clock).Value;
            sp.GetRequiredService<IAgentRepository>().Add(sam);
            var article = KbArticle.Create(null, null, "reset-password", "Reset your password", null, "Steps.", sam.Id, host.Clock).Value;
            sp.GetRequiredService<IKbRepository>().AddArticle(article);
            var ticket = Ticket.Create(TicketNumber.Create("ORB", 1).Value, product.Id, requester.Id, "Cannot log in", TicketChannel.Web, null, false, host.Clock).Value;
            ticket.AddCustomerReply(requester.Id, "<p>Help</p>", host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Open, Actor.ForAgent(sam.Id), host.Clock).IsSuccess.ShouldBeTrue();
            sp.GetRequiredService<ITicketRepository>().Add(ticket);
            (ticketId, articleId) = (ticket.Id, article.Id);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        return new Seed(ticketId, articleId);
    }

    private static IncomingAttachment PngFile() => new("shot.png", "image/png", Png.Length, new MemoryStream(Png));

    private static async Task<Result<AgentMessageResponse>> ReplyAsync(
        PersistenceTestHost host, Guid ticketId, AddAgentReplyRequest request, params IncomingAttachment[] files)
    {
        await using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAddAgentReplyRequestHandler>().HandleAsync(ticketId, request, files, Ct);
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct));
    }

    private string[] FilesUnderRoot() => Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : [];

    [Fact]
    public async Task A_reply_commits_message_attachment_event_article_link_token_and_outbox_together()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host);
        var messagesBefore = await ScalarAsync("SELECT count(*) FROM messages WHERE visibility = 'Public'");

        var result = await ReplyAsync(host, seed.TicketId, new AddAgentReplyRequest("Try **again**", [seed.ArticleId], null, null), PngFile());

        result.IsSuccess.ShouldBeTrue();
        var messageId = result.Value.Message.Id;
        (await ScalarAsync("SELECT count(*) FROM messages WHERE visibility = 'Public'")).ShouldBe(messagesBefore + 1);
        (await ScalarAsync($"SELECT count(*) FROM messages WHERE id = '{messageId}' AND author_type = 'Agent' AND body LIKE '%<strong>again</strong>%'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM attachments WHERE message_id = '{messageId}'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'MessageAdded' AND actor_type = 'Agent'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'StatusChanged' AND payload::text LIKE '%Open%' AND payload::text LIKE '%Pending%'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM ticket_articles WHERE message_id = '{messageId}' AND article_id = '{seed.ArticleId}'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM email_outbox WHERE kind = 'agent-reply' AND payload::text LIKE '%{messageId}%'")).ShouldBe(1);
        FilesUnderRoot().Length.ShouldBe(1);
        result.Value.Ticket.Status.ShouldBe("Pending");
    }

    [Fact]
    public async Task A_failed_commit_leaves_no_message_rows_and_no_files()
    {
        await using var failing = NewHost(services => services.AddScoped<IEmailOutbox>(_ => new ThrowingOutbox(_root)));
        var seed = await SeedAsync(failing);
        var messagesBefore = await ScalarAsync("SELECT count(*) FROM messages");

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => ReplyAsync(failing, seed.TicketId, new AddAgentReplyRequest("Try again", [seed.ArticleId], null, null), PngFile()));

        // The failure came from the outbox, which runs after the file was saved: so the cleanup really had a file to remove.
        thrown.Message.ShouldBe("outbox unavailable");
        ThrowingOutbox.FilesSeen.ShouldBe(1);

        (await ScalarAsync("SELECT count(*) FROM messages")).ShouldBe(messagesBefore);
        (await ScalarAsync("SELECT count(*) FROM attachments")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM ticket_articles")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'StatusChanged' AND payload::text LIKE '%Pending%'")).ShouldBe(0);
        FilesUnderRoot().ShouldBeEmpty();

        await using var healthy = NewHost();
        var retry = await ReplyAsync(healthy, seed.TicketId, new AddAgentReplyRequest("Try again", null, null, null), PngFile());
        retry.IsSuccess.ShouldBeTrue();
        (await ScalarAsync("SELECT count(*) FROM messages")).ShouldBe(messagesBefore + 1);
        FilesUnderRoot().Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_reply_to_an_erased_requester_is_saved_without_an_email()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host, eraseRequester: true);
        var messagesBefore = await ScalarAsync("SELECT count(*) FROM messages");

        var result = await ReplyAsync(host, seed.TicketId, new AddAgentReplyRequest("Sorry to see you go", null, null, null));

        result.IsSuccess.ShouldBeTrue();
        (await ScalarAsync("SELECT count(*) FROM messages")).ShouldBe(messagesBefore + 1);
        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(0);
    }
}

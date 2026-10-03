using Microsoft.EntityFrameworkCore;
using Npgsql;
using TechStrap.Domain.Knowledge;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>Database-level behaviour of the knowledge-base, audit, outbox and idempotency tables.</summary>
public sealed class KnowledgeAndOutboxSchemaTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    [Theory]
    [InlineData("kb_categories")]
    [InlineData("kb_articles")]
    [InlineData("ticket_articles")]
    [InlineData("admin_events")]
    [InlineData("email_outbox")]
    [InlineData("intake_idempotency_keys")]
    public void The_knowledge_audit_and_outbox_tables_are_in_the_model(string table)
    {
        ModelInspector.HasTable(table).ShouldBeTrue();
    }

    [Theory]
    [InlineData("kb_articles", "product_id,slug")]
    [InlineData("kb_categories", "product_id,slug")]
    [InlineData("intake_idempotency_keys", "api_key_id,key_hash")]
    public void The_documented_unique_indexes_exist(string table, string columns)
    {
        ModelInspector.HasUniqueIndex(ModelInspector.Table(table), columns.Split(',')).ShouldBeTrue();
    }

    private static KbArticleRecord Article(Guid? productId, string slug, Guid authorId) => new()
    {
        Id = Guid.CreateVersion7(),
        ProductId = productId,
        Slug = slug,
        Title = "Title",
        BodyMarkdown = "body",
        Status = KbArticleStatus.Draft,
        AuthorId = authorId,
        CreatedAt = RecordSeed.Now,
        UpdatedAt = RecordSeed.Now,
    };

    private static async Task<AgentRecord> AgentAsync(TechStrapDbContext context)
    {
        var agent = new AgentRecord { Id = Guid.CreateVersion7(), OidcSubject = "oidc|a", Email = "a@example.com", Role = TechStrap.Domain.Agents.AgentRole.Agent, IsActive = true };
        context.Set<AgentRecord>().Add(agent);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return agent;
    }

    [Fact]
    public async Task Two_shared_articles_cannot_use_the_same_slug_because_nulls_count_as_equal()
    {
        await using var context = CreateDbContext();
        var agent = await AgentAsync(context);
        context.Set<KbArticleRecord>().Add(Article(null, "reset-password", agent.Id));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Set<KbArticleRecord>().Add(Article(null, "reset-password", agent.Id));
        var failure = await Should.ThrowAsync<DbUpdateException>(async () => await context.SaveChangesAsync(TestContext.Current.CancellationToken));

        (failure.InnerException as PostgresException)!.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task The_same_slug_is_allowed_in_different_products_and_in_the_shared_space()
    {
        await using var context = CreateDbContext();
        var agent = await AgentAsync(context);
        var acme = await RecordSeed.ProductAsync(context, "acme", "ACME");
        var orbitly = await RecordSeed.ProductAsync(context, "orbitly", "ORB");

        context.Set<KbArticleRecord>().AddRange(
            Article(acme.Id, "getting-started", agent.Id),
            Article(orbitly.Id, "getting-started", agent.Id),
            Article(null, "getting-started", agent.Id));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_idempotency_key_is_unique_per_api_key()
    {
        await using var context = CreateDbContext();
        var product = await RecordSeed.ProductAsync(context);
        var requester = await RecordSeed.RequesterAsync(context);
        var ticket = await RecordSeed.TicketAsync(context, product, requester, "ACME-1");
        var key = new ProductApiKeyRecord
        {
            Id = Guid.CreateVersion7(), ProductId = product.Id, Kind = TechStrap.Domain.Products.ApiKeyKind.Trusted, KeyHash = "h", KeyPrefix = "tsk_abcd", CreatedAt = RecordSeed.Now,
        };
        context.Set<ProductApiKeyRecord>().Add(key);
        IntakeIdempotencyKeyRecord Entry() => new()
        {
            Id = Guid.CreateVersion7(), ApiKeyId = key.Id, KeyHash = "same", TicketId = ticket.Id, CreatedAt = RecordSeed.Now,
        };

        context.Set<IntakeIdempotencyKeyRecord>().Add(Entry());
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.Set<IntakeIdempotencyKeyRecord>().Add(Entry());

        var failure = await Should.ThrowAsync<DbUpdateException>(async () => await context.SaveChangesAsync(TestContext.Current.CancellationToken));
        (failure.InnerException as PostgresException)!.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task The_outbox_has_a_partial_index_on_due_pending_rows()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_email_outbox_next_attempt_at_when_pending'";

        var definition = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        definition.ShouldNotBeNull();
        definition.ShouldContain("Pending");
    }

    [Fact]
    public async Task Admin_events_have_no_foreign_keys_so_they_outlive_what_they_describe()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM information_schema.table_constraints WHERE table_name = 'admin_events' AND constraint_type = 'FOREIGN KEY'";

        ((long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).ShouldBe(0);
    }
}

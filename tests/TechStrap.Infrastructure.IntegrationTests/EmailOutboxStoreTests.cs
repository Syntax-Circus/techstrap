using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence.Repositories;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class EmailOutboxStoreTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static EmailOutboxItem NewItem(PersistenceTestHost host, string to = "ann@example.com") =>
        EmailOutboxItem.Enqueue("TicketConfirmation", to, "{\"ticket\":\"ACME-1\"}", null, null, host.Clock).Value;

    private static async Task EnqueueAsync(PersistenceTestHost host, params EmailOutboxItem[] items)
    {
        var result = await host.CommitAsync(sp =>
        {
            foreach (var item in items)
            {
                sp.GetRequiredService<IEmailOutbox>().Enqueue(item);
            }

            return Task.CompletedTask;
        });
        result.IsSuccess.ShouldBeTrue();
    }

    private static Task<IReadOnlyList<EmailOutboxItem>> ClaimAsync(PersistenceTestHost host, string worker, int batch = 10) =>
        host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().ClaimBatchAsync(worker, batch, Lease, Ct));

    [Fact]
    public async Task An_enqueued_row_is_written_by_the_callers_commit_and_round_trips()
    {
        await using var host = new PersistenceTestHost(Database);
        var item = EmailOutboxItem.Enqueue("TicketConfirmation", "Ann@Example.com", "{\"ticket\":\"ACME-1\"}", Guid.NewGuid(), Guid.NewGuid(), host.Clock).Value;

        await EnqueueAsync(host, item);

        var loaded = (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().GetAsync(item.Id, Ct)))!;
        loaded.Kind.ShouldBe("TicketConfirmation");
        loaded.ToAddress.ShouldBe("ann@example.com");
        loaded.PayloadJson.ShouldContain("ACME-1");
        loaded.ProductId.ShouldBe(item.ProductId);
        loaded.TicketId.ShouldBe(item.TicketId);
        loaded.Status.ShouldBe(OutboxStatus.Pending);
    }

    [Fact]
    public async Task Rolling_back_the_callers_unit_of_work_removes_the_enqueued_row()
    {
        await using var host = new PersistenceTestHost(Database);
        var item = NewItem(host);
        await using (var scope = host.CreateScope())
        {
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
            scope.ServiceProvider.GetRequiredService<IEmailOutbox>().Enqueue(item);
        }

        (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().GetAsync(item.Id, Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task A_ticket_and_its_confirmation_email_are_written_together_or_not_at_all()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync();
        var confirmation = NewItem(host);

        var result = await host.CommitAsync(sp =>
        {
            var number = TicketNumber.Create("ACME", 1).Value;
            var duplicate = Ticket.Create(number, scenario.Acme.Id, scenario.Requester.Id, "Duplicate number", TicketChannel.Web, null, false, host.Clock).Value;
            sp.GetRequiredService<ITicketRepository>().Add(duplicate);
            sp.GetRequiredService<IEmailOutbox>().Enqueue(confirmation);
            return Task.CompletedTask;
        });

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.Duplicate);
        (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().GetAsync(confirmation.Id, Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Claiming_returns_only_due_pending_rows_oldest_first_up_to_the_batch_size_and_marks_them_Sending()
    {
        await using var host = new PersistenceTestHost(Database);
        var first = NewItem(host, "a@example.com");
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = NewItem(host, "b@example.com");
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var third = NewItem(host, "c@example.com");
        await EnqueueAsync(host, third, first, second);

        var claimed = await ClaimAsync(host, "worker-1", batch: 2);

        claimed.Select(c => c.Id).ShouldBe([first.Id, second.Id]);
        claimed.ShouldAllBe(c => c.Status == OutboxStatus.Sending && c.Attempts == 1 && c.ClaimedBy == "worker-1");
        var stored = (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().GetAsync(first.Id, Ct)))!;
        stored.Status.ShouldBe(OutboxStatus.Sending);
        stored.LockedUntil.ShouldBe(host.Clock.GetUtcNow() + Lease);
        (await ClaimAsync(host, "worker-2")).Select(c => c.Id).ShouldBe([third.Id]);
    }

    [Fact]
    public async Task A_row_scheduled_for_later_is_not_claimed_until_it_is_due()
    {
        await using var host = new PersistenceTestHost(Database);
        var item = NewItem(host);
        await EnqueueAsync(host, item);
        (await ClaimAsync(host, "worker-1")).ShouldHaveSingleItem();
        (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().MarkFailedAsync(item.Id, "worker-1", "smtp down", Ct))).IsSuccess.ShouldBeTrue();

        (await ClaimAsync(host, "worker-1")).ShouldBeEmpty();
        host.Clock.Advance(OutboxRetryPolicy.BaseDelay);
        (await ClaimAsync(host, "worker-1")).ShouldHaveSingleItem().Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_claimers_never_receive_the_same_row()
    {
        await using var host = new PersistenceTestHost(Database);
        var items = Enumerable.Range(0, 100).Select(i => NewItem(host, $"user{i}@example.com")).ToArray();
        await EnqueueAsync(host, items);
        var start = new TaskCompletionSource();

        var claimers = Enumerable.Range(0, 8).Select(async n =>
        {
            await start.Task;
            var mine = new List<Guid>();
            while (await ClaimAsync(host, $"worker-{n}", batch: 7) is { Count: > 0 } batch)
            {
                mine.AddRange(batch.Select(b => b.Id));
            }

            return mine;
        }).ToList();
        start.SetResult();
        var results = await Task.WhenAll(claimers);

        var all = results.SelectMany(r => r).ToList();
        all.Count.ShouldBe(100);
        all.Distinct().Count().ShouldBe(100);
        results.Count(r => r.Count > 0).ShouldBeGreaterThan(1, "the work should have been shared between claimers");
    }

    [Fact]
    public async Task An_unexpired_claim_is_not_reclaimable_but_an_expired_one_is()
    {
        await using var host = new PersistenceTestHost(Database);
        var item = NewItem(host);
        await EnqueueAsync(host, item);
        (await ClaimAsync(host, "worker-1")).ShouldHaveSingleItem();

        (await ClaimAsync(host, "worker-2")).ShouldBeEmpty();
        host.Clock.Advance(Lease);
        var reclaimed = (await ClaimAsync(host, "worker-2")).ShouldHaveSingleItem();

        reclaimed.ClaimedBy.ShouldBe("worker-2");
        reclaimed.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Acknowledging_marks_the_row_Sent_and_a_worker_that_lost_its_claim_cannot_acknowledge()
    {
        await using var host = new PersistenceTestHost(Database);
        var item = NewItem(host);
        await EnqueueAsync(host, item);
        await ClaimAsync(host, "worker-1");
        host.Clock.Advance(Lease);
        await ClaimAsync(host, "worker-2");

        var stale = await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().MarkSentAsync(item.Id, "worker-1", Ct));
        var untouched = (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().GetAsync(item.Id, Ct)))!;
        var current = await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().MarkSentAsync(item.Id, "worker-2", Ct));

        stale.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.OutboxNotClaimOwner);
        stale.Errors[0].Kind.ShouldBe(ResultErrorKind.Conflict);
        untouched.Status.ShouldBe(OutboxStatus.Sending);
        untouched.ClaimedBy.ShouldBe("worker-2");
        untouched.SentAt.ShouldBeNull();
        current.IsSuccess.ShouldBeTrue();
        var stored = (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().GetAsync(item.Id, Ct)))!;
        stored.Status.ShouldBe(OutboxStatus.Sent);
        stored.SentAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Acknowledging_an_unknown_row_is_not_found()
    {
        await using var host = new PersistenceTestHost(Database);

        var result = await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().MarkSentAsync(Guid.NewGuid(), "worker-1", Ct));

        result.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task Repeated_failures_back_off_then_dead_letter_and_dead_letters_are_listed_newest_first()
    {
        await using var host = new PersistenceTestHost(Database);
        var older = NewItem(host, "older@example.com");
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var newer = NewItem(host, "newer@example.com");
        await EnqueueAsync(host, older, newer);

        for (var attempt = 1; attempt <= OutboxRetryPolicy.MaxAttempts; attempt++)
        {
            host.Clock.Advance(OutboxRetryPolicy.MaxDelay);
            var claimed = await ClaimAsync(host, "worker-1");
            claimed.Count.ShouldBe(2);
            foreach (var item in claimed)
            {
                (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().MarkFailedAsync(item.Id, "worker-1", $"failure {attempt}", Ct))).IsSuccess.ShouldBeTrue();
            }
        }

        var deadLetters = await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().ListDeadLettersAsync(1, 10, Ct));

        deadLetters.TotalCount.ShouldBe(2);
        deadLetters.Items.Select(d => d.ToAddress).ShouldBe(["newer@example.com", "older@example.com"]);
        deadLetters.Items.ShouldAllBe(d => d.Status == OutboxStatus.DeadLettered && d.Attempts == OutboxRetryPolicy.MaxAttempts && d.LastError == "failure 5");
        host.Clock.Advance(OutboxRetryPolicy.MaxDelay);
        (await ClaimAsync(host, "worker-1")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_dead_letter_can_be_retried_or_discarded_through_the_callers_commit()
    {
        await using var host = new PersistenceTestHost(Database);
        var retried = NewItem(host, "retry@example.com");
        var discarded = NewItem(host, "discard@example.com");
        await EnqueueAsync(host, retried, discarded);
        for (var attempt = 1; attempt <= OutboxRetryPolicy.MaxAttempts; attempt++)
        {
            host.Clock.Advance(OutboxRetryPolicy.MaxDelay);
            foreach (var item in await ClaimAsync(host, "worker-1"))
            {
                await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().MarkFailedAsync(item.Id, "worker-1", "boom", Ct));
            }
        }

        var result = await host.CommitAsync(async sp =>
        {
            var store = sp.GetRequiredService<IEmailOutboxStore>();
            var toRetry = (await store.GetAsync(retried.Id, Ct))!;
            toRetry.Retry(host.Clock).IsSuccess.ShouldBeTrue();
            store.Update(toRetry);
            var toDiscard = (await store.GetAsync(discarded.Id, Ct))!;
            toDiscard.Discard().IsSuccess.ShouldBeTrue();
            store.Update(toDiscard);
        });

        result.IsSuccess.ShouldBeTrue();
        (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().ListDeadLettersAsync(1, 10, Ct))).TotalCount.ShouldBe(0);
        (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().GetAsync(discarded.Id, Ct)))!.Status.ShouldBe(OutboxStatus.Discarded);
        var reclaimed = (await ClaimAsync(host, "worker-2")).ShouldHaveSingleItem();
        reclaimed.Id.ShouldBe(retried.Id);
        reclaimed.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task Sent_rows_are_neither_dead_letters_nor_claimable()
    {
        await using var host = new PersistenceTestHost(Database);
        var item = NewItem(host);
        await EnqueueAsync(host, item);
        await ClaimAsync(host, "worker-1");
        await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().MarkSentAsync(item.Id, "worker-1", Ct));

        (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().ListDeadLettersAsync(1, 10, Ct))).TotalCount.ShouldBe(0);
        (await ClaimAsync(host, "worker-1")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_worker_that_lost_its_claim_cannot_record_a_failure_either_and_the_row_is_left_unchanged()
    {
        await using var host = new PersistenceTestHost(Database);
        var item = NewItem(host);
        await EnqueueAsync(host, item);
        await ClaimAsync(host, "worker-1");
        host.Clock.Advance(Lease);
        await ClaimAsync(host, "worker-2");
        var before = (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().GetAsync(item.Id, Ct)))!;

        var stale = await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().MarkFailedAsync(item.Id, "worker-1", "late failure", Ct));

        stale.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.OutboxNotClaimOwner);
        stale.Errors[0].Kind.ShouldBe(ResultErrorKind.Conflict);
        var after = (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().GetAsync(item.Id, Ct)))!;
        after.Status.ShouldBe(OutboxStatus.Sending);
        after.ClaimedBy.ShouldBe("worker-2");
        after.Attempts.ShouldBe(before.Attempts);
        after.LastError.ShouldBe(before.LastError);
        after.LockedUntil.ShouldBe(before.LockedUntil);
        after.NextAttemptAt.ShouldBe(before.NextAttemptAt);
    }

    [Fact]
    public async Task An_item_whose_expired_leases_reach_the_maximum_attempts_is_dead_lettered_by_the_claim_and_left_out_of_the_batch()
    {
        await using var host = new PersistenceTestHost(Database);
        var crashing = NewItem(host, "crash@example.com");
        await EnqueueAsync(host, crashing);
        for (var attempt = 1; attempt <= OutboxRetryPolicy.MaxAttempts; attempt++)
        {
            (await ClaimAsync(host, $"worker-{attempt}")).ShouldHaveSingleItem().Attempts.ShouldBe(attempt);
            host.Clock.Advance(Lease);
        }

        var batch = await ClaimAsync(host, "worker-final");

        batch.ShouldBeEmpty();
        var stored = (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().GetAsync(crashing.Id, Ct)))!;
        stored.Status.ShouldBe(OutboxStatus.DeadLettered);
        stored.Attempts.ShouldBe(OutboxRetryPolicy.MaxAttempts);
        stored.ClaimedBy.ShouldBeNull();
        stored.LockedUntil.ShouldBeNull();
        stored.LastError.ShouldBe("worker lease expired");
        (await ClaimAsync(host, "worker-final")).ShouldBeEmpty();
        (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().ListDeadLettersAsync(1, 10, Ct))).Items.ShouldHaveSingleItem().Id.ShouldBe(crashing.Id);
    }

    [Fact]
    public async Task A_dead_lettered_expired_item_does_not_stop_the_other_due_rows_in_the_same_claim()
    {
        await using var host = new PersistenceTestHost(Database);
        var crashing = NewItem(host, "crash@example.com");
        await EnqueueAsync(host, crashing);
        for (var attempt = 1; attempt <= OutboxRetryPolicy.MaxAttempts; attempt++)
        {
            await ClaimAsync(host, $"worker-{attempt}");
            host.Clock.Advance(Lease);
        }

        var healthy = NewItem(host, "ok@example.com");
        await EnqueueAsync(host, healthy);

        var batch = await ClaimAsync(host, "worker-final");

        batch.ShouldHaveSingleItem().Id.ShouldBe(healthy.Id);
        (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().GetAsync(crashing.Id, Ct)))!.Status.ShouldBe(OutboxStatus.DeadLettered);
    }

    [Theory]
    [InlineData(0, Paging.DefaultBatchSize)]
    [InlineData(-5, Paging.DefaultBatchSize)]
    [InlineData(5, 5)]
    public async Task The_batch_size_is_normalized_through_Paging(int requested, int expected)
    {
        await using var host = new PersistenceTestHost(Database);
        await EnqueueAsync(host, [.. Enumerable.Range(0, Paging.DefaultBatchSize + 5).Select(i => NewItem(host, $"u{i}@example.com"))]);

        (await ClaimAsync(host, "worker-1", requested)).Count.ShouldBe(expected);
    }

    [Fact]
    public async Task A_batch_size_above_the_maximum_is_capped()
    {
        await using var host = new PersistenceTestHost(Database);
        await EnqueueAsync(host, [.. Enumerable.Range(0, Paging.MaxBatchSize + 3).Select(i => NewItem(host, $"u{i}@example.com"))]);

        (await ClaimAsync(host, "worker-1", int.MaxValue)).Count.ShouldBe(Paging.MaxBatchSize);
    }

    [Fact]
    public async Task Dead_letters_with_the_same_creation_time_are_listed_in_Id_order_and_paging_is_normalized()
    {
        await using var host = new PersistenceTestHost(Database);
        var items = Enumerable.Range(0, 3).Select(i => NewItem(host, $"same{i}@example.com")).ToArray();
        await EnqueueAsync(host, items);
        for (var attempt = 1; attempt <= OutboxRetryPolicy.MaxAttempts; attempt++)
        {
            host.Clock.Advance(OutboxRetryPolicy.MaxDelay);
            foreach (var item in await ClaimAsync(host, "worker-1"))
            {
                await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().MarkFailedAsync(item.Id, "worker-1", "boom", Ct));
            }
        }

        var all = await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().ListDeadLettersAsync(1, 10, Ct));
        var clamped = await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().ListDeadLettersAsync(-3, 0, Ct));

        all.Items.Select(i => i.Id).ShouldBe(items.Select(i => i.Id).Order());
        clamped.Page.ShouldBe(1);
        clamped.PageSize.ShouldBe(Paging.DefaultPageSize);
        clamped.Items.Count.ShouldBe(3);
    }

    private async Task<string> ExplainClaimAsync(DateTimeOffset now)
    {
        await using var context = Database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("ANALYZE email_outbox", Ct);
        await using var transaction = await context.Database.BeginTransactionAsync(Ct);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off", Ct);

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "EXPLAIN " + EmailOutboxStore.ClaimSql.Replace("{0}", "@p0", StringComparison.Ordinal).Replace("{1}", "@p1", StringComparison.Ordinal);
        command.Parameters.Add(new NpgsqlParameter("p0", now));
        command.Parameters.Add(new NpgsqlParameter("p1", 20));
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join('\n', lines);
    }

    [Fact]
    public async Task The_claim_query_uses_the_partial_indexes_for_both_the_Pending_and_the_Sending_branch()
    {
        await using var host = new PersistenceTestHost(Database);
        var now = host.Clock.GetUtcNow();
        await using (var connection = new NpgsqlConnection(Database.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO email_outbox (id, kind, to_address, payload, status, attempts, next_attempt_at, claimed_by, locked_until, created_at)
                SELECT gen_random_uuid(), 'K', 'a' || g || '@example.com', '{}',
                       CASE WHEN g % 100 = 1 THEN 'Pending' WHEN g % 100 = 2 THEN 'Sending' ELSE 'Sent' END,
                       1, @now - interval '1 hour', CASE WHEN g % 100 = 2 THEN 'w' END, CASE WHEN g % 100 = 2 THEN @now - interval '1 minute' END, @now
                FROM generate_series(1, 20000) AS g
                """;
            insert.Parameters.AddWithValue("now", now);
            await insert.ExecuteNonQueryAsync(Ct);
        }

        var plan = await ExplainClaimAsync(now);

        plan.ShouldContain("ix_email_outbox_next_attempt_at_when_pending", customMessage: plan);
        plan.ShouldContain("ix_email_outbox_locked_until_when_sending", customMessage: plan);
        plan.ShouldNotContain("Seq Scan");
    }

    [Fact]
    public async Task An_oversized_payload_is_refused_by_the_Domain_before_it_can_be_enqueued()
    {
        await using var host = new PersistenceTestHost(Database);
        var payload = "{\"p\":\"" + new string('x', DomainLimits.OutboxPayloadMaxLength) + "\"}";

        EmailOutboxItem.Enqueue("K", "a@example.com", payload, null, null, host.Clock).Error!.Code.ShouldBe("payload-too-long");
    }

}

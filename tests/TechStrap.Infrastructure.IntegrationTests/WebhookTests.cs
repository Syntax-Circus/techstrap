using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TechStrap.Application.Email;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Application.Webhooks;
using TechStrap.Contracts.Webhooks;
using TechStrap.Domain.Features;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;
using TechStrap.Infrastructure.Persistence.Repositories;
using TechStrap.Infrastructure.Webhooks;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class WebhookTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid ActorId = Guid.NewGuid();
    private static WebhookEndpointRequest Endpoint => new() { Url = "https://discord.com/api/webhooks/123/secret" };
    private static UnitOfWork Uow(TechStrapDbContext db) => new(db, new WebhookCapture(db, Options.Create(new AdminLinkOptions { PublicUrl = "https://admin.example.com" }), TimeProvider.System));
    private static WebhookStore Store(TechStrapDbContext db) => new(db, Uow(db), TimeProvider.System);
    private static WebhookDeliveryProcessor Processor(TechStrapDbContext db, IWebhookSender sender) => new(db, sender, TimeProvider.System, Options.Create(new OutboxRetentionOptions()));

    [Fact]
    public async Task Settings_mask_secrets_retain_unchanged_urls_and_reject_stale_edits()
    {
        await using var db = CreateDbContext();
        var store = Store(db);
        var saved = await store.UpdateAsync(null, new("Replace", 0, [Endpoint]), ActorId, Ct);
        saved.IsSuccess.ShouldBeTrue();
        saved.Value.Endpoints[0].Settings.Url.ShouldBeNull();
        JsonSerializer.Serialize(saved.Value).ShouldNotContain("secret");
        var unchanged = saved.Value.Endpoints.Select(x => x.Settings).ToArray();
        (await store.UpdateAsync(null, new("Replace", saved.Value.Version, unchanged), ActorId, Ct)).IsSuccess.ShouldBeTrue();
        (await store.UpdateAsync(null, new("Replace", saved.Value.Version, unchanged), ActorId, Ct)).IsFailure.ShouldBeTrue();
        db.ChangeTracker.Clear();
        (await db.Set<WebhookSettingsRecord>().SingleAsync(Ct)).Endpoints.ShouldContain("secret");
    }

    [Fact]
    public async Task Creation_and_status_changes_queue_atomically_without_body_or_requester_data()
    {
        await using var db = CreateDbContext();
        var product = await RecordSeed.ProductAsync(db);
        var requester = await RecordSeed.RequesterAsync(db);
        (await Store(db).UpdateAsync(null, new("Replace", 0, [Endpoint with { TicketStatuses = ["Open"] }]), ActorId, Ct)).IsSuccess.ShouldBeTrue();
        await using (var tx = await Uow(db).BeginAsync(Ct))
        {
            var ticket = new TicketRecord { Id = Guid.CreateVersion7(), ProductId = product.Id, RequesterId = requester.Id, Number = "ACME-1", Subject = "Help", Status = TicketStatus.New, CreatedAt = DateTimeOffset.UtcNow, LastActivityAt = DateTimeOffset.UtcNow };
            db.Add(ticket);
            db.Add(new TicketEventRecord { Id = Guid.CreateVersion7(), TicketId = ticket.Id, Type = TicketEventType.Created, OccurredAt = DateTimeOffset.UtcNow });
            (await tx.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
        }
        var row = await db.Set<WebhookDeliveryRecord>().SingleAsync(Ct);
        row.Payload.ShouldContain("ticket.created");
        row.Payload.ShouldContain("https://admin.example.com/tickets/ACME-1");
        row.Payload.ShouldNotContain(requester.Email);
        await using (var tx = await Uow(db).BeginAsync(Ct))
        {
            var ticket = await db.Set<TicketRecord>().SingleAsync(Ct);
            ticket.Status = TicketStatus.Open;
            db.Add(new TicketEventRecord { Id = Guid.CreateVersion7(), TicketId = ticket.Id, Type = TicketEventType.StatusChanged, Payload = "{\"from\":\"New\",\"to\":\"Open\"}", OccurredAt = DateTimeOffset.UtcNow });
            (await tx.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
        }
        (await db.Set<WebhookDeliveryRecord>().CountAsync(Ct)).ShouldBe(2);
        await using (var tx = await Uow(db).BeginAsync(Ct)) { (await tx.CommitAsync(Ct)).IsSuccess.ShouldBeTrue(); }
        (await db.Set<WebhookDeliveryRecord>().CountAsync(Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Feature_drafts_notify_on_creation_and_selected_progress_but_not_publication_or_rollback()
    {
        await using var db = CreateDbContext();
        var product = await RecordSeed.ProductAsync(db);
        (await Store(db).UpdateAsync(null, new("Replace", 0, [Endpoint with { FeatureStatuses = ["Shipped"] }]), ActorId, Ct)).IsSuccess.ShouldBeTrue();
        await using (var tx = await Uow(db).BeginAsync(Ct))
        {
            db.Add(new FeatureRecord { Id = Guid.CreateVersion7(), ProductId = product.Id, Slug = "dark-mode", Title = "Dark mode", DescriptionMarkdown = "Private description", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            (await tx.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
        }
        (await db.Set<WebhookDeliveryRecord>().SingleAsync(Ct)).Payload.ShouldNotContain("Private description");
        await using (var tx = await Uow(db).BeginAsync(Ct))
        {
            (await db.Set<FeatureRecord>().SingleAsync(Ct)).PublicationState = FeaturePublicationState.Published;
            (await tx.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
        }
        (await db.Set<WebhookDeliveryRecord>().CountAsync(Ct)).ShouldBe(1);
        await using (await Uow(db).BeginAsync(Ct))
        {
            (await db.Set<FeatureRecord>().SingleAsync(Ct)).ProgressStatus = FeatureProgressStatus.Shipped;
            await new WebhookCapture(db, Options.Create(new AdminLinkOptions()), TimeProvider.System).StageAsync(Ct);
            await db.SaveChangesAsync(Ct);
        }
        (await db.Set<WebhookDeliveryRecord>().CountAsync(Ct)).ShouldBe(1);
        await using (var tx = await Uow(db).BeginAsync(Ct))
        {
            (await db.Set<FeatureRecord>().SingleAsync(Ct)).ProgressStatus = FeatureProgressStatus.Shipped;
            (await tx.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
        }
        (await db.Set<WebhookDeliveryRecord>().CountAsync(Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Product_replacement_disables_global_delivery_and_disabling_cancels_queued_snapshots()
    {
        await using var db = CreateDbContext();
        var product = await RecordSeed.ProductAsync(db);
        var global = (await Store(db).UpdateAsync(null, new("Replace", 0, [Endpoint]), ActorId, Ct)).Value;
        (await Store(db).GetAsync(product.Id, Ct)).Value.EffectiveEndpoints.Count.ShouldBe(1);
        (await Store(db).UpdateAsync(product.Id, new("Replace", 0, [Endpoint with { Name = "Product" }]), ActorId, Ct)).IsSuccess.ShouldBeTrue();
        var settings = (await Store(db).GetAsync(product.Id, Ct)).Value;
        settings.EffectiveEndpoints[0].Settings.Name.ShouldBe("Product");
        var test = await Store(db).ActAsync(product.Id, new("Test", settings.Endpoints[0].Settings.Id), ActorId, Ct);
        test.IsSuccess.ShouldBeTrue();
        (await Store(db).UpdateAsync(product.Id, new("Disabled", settings.Version, settings.Endpoints.Select(x => x.Settings).ToArray()), ActorId, Ct)).IsSuccess.ShouldBeTrue();
        (await db.Set<WebhookDeliveryRecord>().SingleAsync(Ct)).Status.ShouldBe("Discarded");
        (await Store(db).GetAsync(product.Id, Ct)).Value.EffectiveEndpoints.ShouldBeEmpty();
        (await Store(db).GetAsync(null, Ct)).Value.Version.ShouldBe(global.Version);
    }

    [Fact]
    public async Task Worker_retries_then_delivers_and_does_not_send_twice()
    {
        await using var db = CreateDbContext();
        var settings = (await Store(db).UpdateAsync(null, new("Replace", 0, [Endpoint]), ActorId, Ct)).Value;
        (await Store(db).ActAsync(null, new("Test", settings.Endpoints[0].Settings.Id), ActorId, Ct)).IsSuccess.ShouldBeTrue();
        var sender = new FakeSender(new(false, true, "http-429", TimeSpan.FromMinutes(10)));
        (await Processor(db, sender).DrainAsync(Ct)).ShouldBe(1);
        db.ChangeTracker.Clear();
        var row = await db.Set<WebhookDeliveryRecord>().SingleAsync(Ct);
        row.Status.ShouldBe("Pending");
        row.NextAttemptAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(9));
        row.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync(Ct);
        sender.Result = new(true, false, null);
        (await Processor(db, sender).DrainAsync(Ct)).ShouldBe(1);
        (await Processor(db, sender).DrainAsync(Ct)).ShouldBe(0);
        db.ChangeTracker.Clear();
        row = await db.Set<WebhookDeliveryRecord>().SingleAsync(Ct);
        row.Status.ShouldBe("Sent");
        row.Url.ShouldBeEmpty();
        sender.Calls.ShouldBe(2);
    }

    [Fact]
    public void Discord_payload_suppresses_mentions_and_bounds_titles()
    {
        var payload = new WebhookEventDto(1, Guid.NewGuid(), "ticket.created", DateTimeOffset.UtcNow, new(Guid.NewGuid(), "test", "Test"), new(Guid.NewGuid(), "Ticket", new string('x', 1000) + "@everyone", "New", null, "TEST-1", "Support", null, null));
        using var json = JsonDocument.Parse(WebhookJson.Write(WebhookSender.DiscordPayload(payload)));
        json.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength().ShouldBe(0);
        json.RootElement.GetProperty("embeds")[0].GetProperty("title").GetString()!.Length.ShouldBe(256);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Requester_erasure_or_ticket_deletion_removes_all_ticket_webhook_snapshots(bool deleteTicket)
    {
        await using var db = CreateDbContext();
        var product = await RecordSeed.ProductAsync(db);
        var requester = await RecordSeed.RequesterAsync(db);
        var ticket = await RecordSeed.TicketAsync(db, product, requester, "ACME-1");
        var settings = (await Store(db).UpdateAsync(null, new("Replace", 0, [Endpoint]), ActorId, Ct)).Value;
        var endpoint = settings.Endpoints[0].Settings with { Url = Endpoint.Url };
        var payload = new WebhookEventDto(1, Guid.NewGuid(), "ticket.created", DateTimeOffset.UtcNow, new(product.Id, product.Key, product.Name), new(ticket.Id, "Ticket", ticket.Subject, "New", null, ticket.Number, "Support", null, null));
        db.Add(WebhookCapture.NewDelivery(WebhookPolicy.GlobalScope, endpoint, payload, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(Ct);
        await using (var tx = await Uow(db).BeginAsync(Ct))
        {
            if (deleteTicket) db.Remove(ticket);
            else await new RequesterErasure(db).EraseDataAsync(requester.Id, requester.Email, DateTimeOffset.UtcNow, Ct);
            (await tx.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
        }
        (await db.Set<WebhookDeliveryRecord>().CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_stale_retry_cannot_resurrect_a_concurrently_discarded_delivery()
    {
        await using var first = CreateDbContext();
        var config = (await Store(first).UpdateAsync(null, new("Replace", 0, [Endpoint]), ActorId, Ct)).Value;
        (await Store(first).ActAsync(null, new("Test", config.Endpoints[0].Settings.Id), ActorId, Ct)).IsSuccess.ShouldBeTrue();
        await Processor(first, new FakeSender(new(false, false, "http-404"))).DrainAsync(Ct);
        first.ChangeTracker.Clear();
        await using var second = CreateDbContext();
        var discarded = await first.Set<WebhookDeliveryRecord>().SingleAsync(Ct);
        var retried = await second.Set<WebhookDeliveryRecord>().SingleAsync(Ct);
        discarded.Status = "Discarded"; discarded.Url = "";
        await first.SaveChangesAsync(Ct);
        retried.Status = "Pending";
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
        first.ChangeTracker.Clear();
        (await first.Set<WebhookDeliveryRecord>().SingleAsync(Ct)).Status.ShouldBe("Discarded");
    }

    [Fact]
    public async Task Webhook_transport_does_not_emit_credential_bearing_http_activities()
    {
        var activities = new System.Collections.Concurrent.ConcurrentBag<System.Diagnostics.Activity>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "System.Net.Http",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity),
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        using var handler = WebhookSender.CreateHandler();
        handler.ConnectCallback = (_, _) => throw new HttpRequestException("test-connect-failed");
        using var client = new HttpClient(handler);
        await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync("https://example.com/secret-token", Ct));
        activities.ShouldNotContain(a => a.Tags.Any(t => t.Value != null && t.Value.Contains("secret-token", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Competing_workers_do_not_send_the_same_active_claim()
    {
        await using var first = CreateDbContext();
        var config = (await Store(first).UpdateAsync(null, new("Replace", 0, [Endpoint]), ActorId, Ct)).Value;
        (await Store(first).ActAsync(null, new("Test", config.Endpoints[0].Settings.Id), ActorId, Ct)).IsSuccess.ShouldBeTrue();
        var sender = new BlockingSender();
        var processing = Processor(first, sender).DrainAsync(Ct);
        await sender.Started.Task.WaitAsync(Ct);
        await using var second = CreateDbContext();
        (await Processor(second, new FakeSender(new(true, false, null))).DrainAsync(Ct)).ShouldBe(0);
        sender.Release.TrySetResult();
        (await processing).ShouldBe(1);
    }

    [Fact]
    public async Task Expired_final_claim_becomes_dead_letter_and_can_be_retried_and_retained_history_purged()
    {
        await using var db = CreateDbContext();
        var config = (await Store(db).UpdateAsync(null, new("Replace", 0, [Endpoint]), ActorId, Ct)).Value;
        (await Store(db).ActAsync(null, new("Test", config.Endpoints[0].Settings.Id), ActorId, Ct)).IsSuccess.ShouldBeTrue();
        var row = await db.Set<WebhookDeliveryRecord>().SingleAsync(Ct);
        row.Status = "Sending"; row.ClaimId = Guid.NewGuid(); row.Attempts = 5; row.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync(Ct);
        var sender = new FakeSender(new(true, false, null));
        await Processor(db, sender).DrainAsync(Ct);
        sender.Calls.ShouldBe(0);
        db.ChangeTracker.Clear();
        row = await db.Set<WebhookDeliveryRecord>().SingleAsync(Ct);
        row.Status.ShouldBe("DeadLettered");
        (await Store(db).ActAsync(null, new("Retry", row.Id), ActorId, Ct)).IsSuccess.ShouldBeTrue();
        await Processor(db, sender).DrainAsync(Ct);
        sender.Calls.ShouldBe(1);
        db.ChangeTracker.Clear();
        row = await db.Set<WebhookDeliveryRecord>().SingleAsync(Ct);
        row.Status.ShouldBe("Sent"); row.Attempts.ShouldBe(1);
        row.CreatedAt = DateTimeOffset.UtcNow.AddDays(-100);
        await db.SaveChangesAsync(Ct);
        await Processor(db, sender).PurgeAsync(Ct);
        (await db.Set<WebhookDeliveryRecord>().CountAsync(Ct)).ShouldBe(0);
    }

    private sealed class BlockingSender : IWebhookSender
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<WebhookSendResult> SendAsync(string url, string format, WebhookEventDto payload, CancellationToken cancellationToken)
        {
            Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); return new(true, false, null);
        }
    }

    private sealed class FakeSender(WebhookSendResult result) : IWebhookSender
    {
        internal WebhookSendResult Result { get; set; } = result;
        internal int Calls { get; private set; }
        public Task<WebhookSendResult> SendAsync(string url, string format, WebhookEventDto payload, CancellationToken cancellationToken) { Calls++; return Task.FromResult(Result); }
    }
}

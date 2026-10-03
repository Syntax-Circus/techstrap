using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tests.Outbox;

public sealed class EmailOutboxItemTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    private EmailOutboxItem Queued() =>
        EmailOutboxItem.Enqueue("TicketConfirmation", "Ann@Example.com", "{\"ticket\":\"ACME-1\"}", Guid.NewGuid(), Guid.NewGuid(), _clock).Value;

    private EmailOutboxItem Claimed()
    {
        var item = Queued();
        item.Claim("worker-1", Lease, _clock).IsSuccess.ShouldBeTrue();
        return item;
    }

    [Fact]
    public void A_queued_email_is_pending_and_due_immediately()
    {
        var item = Queued();

        item.Status.ShouldBe(OutboxStatus.Pending);
        item.Attempts.ShouldBe(0);
        item.ToAddress.ShouldBe("ann@example.com");
        item.IsClaimable(_clock).ShouldBeTrue();
    }

    [Fact]
    public void The_kind_the_address_and_the_payload_are_validated()
    {
        EmailOutboxItem.Enqueue("", "a@example.com", null, null, null, _clock).Error!.Code.ShouldBe("kind-required");
        EmailOutboxItem.Enqueue("K", "nope", null, null, null, _clock).Error!.Code.ShouldBe("to-address-invalid");
        EmailOutboxItem.Enqueue("K", "a@example.com", "[1]", null, null, _clock).Error!.Code.ShouldBe("payload-invalid");
        EmailOutboxItem.Enqueue("K", "a@example.com", null, null, null, _clock).Value.PayloadJson.ShouldBe("{}");
    }

    [Fact]
    public void A_payload_over_the_maximum_length_is_rejected_and_one_at_the_maximum_is_accepted()
    {
        var atLimit = "{\"p\":\"" + new string('x', DomainLimits.OutboxPayloadMaxLength - 8) + "\"}";
        atLimit.Length.ShouldBe(DomainLimits.OutboxPayloadMaxLength);

        EmailOutboxItem.Enqueue("K", "a@example.com", atLimit, null, null, _clock).IsSuccess.ShouldBeTrue();
        var rejected = EmailOutboxItem.Enqueue("K", "a@example.com", atLimit.Insert(atLimit.Length - 2, "x"), null, null, _clock);

        rejected.Error!.Code.ShouldBe("payload-too-long");
        rejected.Error.Kind.ShouldBe(DomainErrorKind.Validation);
    }

    [Fact]
    public void Claiming_marks_it_Sending_counts_an_attempt_and_sets_the_lease()
    {
        var item = Claimed();

        item.Status.ShouldBe(OutboxStatus.Sending);
        item.Attempts.ShouldBe(1);
        item.ClaimedBy.ShouldBe("worker-1");
        item.LockedUntil.ShouldBe(_clock.GetUtcNow() + Lease);
    }

    [Fact]
    public void A_claimed_email_cannot_be_claimed_again_until_the_lease_expires()
    {
        var item = Claimed();

        item.Claim("worker-2", Lease, _clock).Error!.Code.ShouldBe("outbox-not-claimable");
        _clock.Advance(Lease);
        item.IsClaimable(_clock).ShouldBeTrue();
        item.Claim("worker-2", Lease, _clock).IsSuccess.ShouldBeTrue();
        item.Attempts.ShouldBe(2);
        item.ClaimedBy.ShouldBe("worker-2");
    }

    [Fact]
    public void A_future_next_attempt_is_not_claimable()
    {
        var item = Claimed();
        item.MarkFailed("worker-1", "smtp down", _clock);

        item.IsClaimable(_clock).ShouldBeFalse();
        _clock.Advance(OutboxRetryPolicy.BaseDelay);
        item.IsClaimable(_clock).ShouldBeTrue();
    }

    [Fact]
    public void Marking_sent_records_the_time_and_releases_the_claim()
    {
        var item = Claimed();
        _clock.Advance(TimeSpan.FromSeconds(2));

        item.MarkSent("worker-1", _clock).IsSuccess.ShouldBeTrue();

        item.Status.ShouldBe(OutboxStatus.Sent);
        item.SentAt.ShouldBe(_clock.GetUtcNow());
        item.ClaimedBy.ShouldBeNull();
        item.LockedUntil.ShouldBeNull();
    }

    [Fact]
    public void Only_a_claimed_email_can_be_marked_sent_or_failed()
    {
        Queued().MarkSent("worker-1", _clock).Error!.Code.ShouldBe("outbox-not-sending");
        Queued().MarkFailed("worker-1", "x", _clock).Error!.Code.ShouldBe("outbox-not-sending");
    }

    [Fact]
    public void Backoff_doubles_from_one_minute_and_the_fifth_failure_dead_letters()
    {
        var item = Queued();
        var expectedDelays = new[] { 1, 2, 4, 8 };

        foreach (var minutes in expectedDelays)
        {
            item.Claim("w", Lease, _clock).IsSuccess.ShouldBeTrue();
            var failedAt = _clock.GetUtcNow();
            item.MarkFailed("w", "boom", _clock);

            item.Status.ShouldBe(OutboxStatus.Pending);
            item.NextAttemptAt.ShouldBe(failedAt.AddMinutes(minutes));
            _clock.Advance(TimeSpan.FromMinutes(minutes));
        }

        item.Claim("w", Lease, _clock);
        item.MarkFailed("w", "final failure", _clock);

        item.Attempts.ShouldBe(OutboxRetryPolicy.MaxAttempts);
        item.Status.ShouldBe(OutboxStatus.DeadLettered);
        item.LastError.ShouldBe("final failure");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 8)]
    [InlineData(7, 60)]
    [InlineData(40, 60)]
    public void The_delay_is_capped_at_one_hour(int attempts, int expectedMinutes)
    {
        OutboxRetryPolicy.DelayAfter(attempts).ShouldBe(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Fact]
    public void A_long_error_is_truncated_to_the_column_limit()
    {
        var item = Claimed();

        item.MarkFailed("worker-1", new string('x', 5000), _clock);

        item.LastError!.Length.ShouldBe(2000);
    }

    [Fact]
    public void A_dead_letter_can_be_retried_with_a_fresh_attempt_count()
    {
        var item = DeadLetter();

        item.Retry(_clock).IsSuccess.ShouldBeTrue();

        item.Status.ShouldBe(OutboxStatus.Pending);
        item.Attempts.ShouldBe(0);
        item.IsClaimable(_clock).ShouldBeTrue();
    }

    [Fact]
    public void A_dead_letter_can_be_discarded_and_nothing_else_can()
    {
        var item = DeadLetter();

        item.Discard().IsSuccess.ShouldBeTrue();

        item.Status.ShouldBe(OutboxStatus.Discarded);
        Queued().Discard().Error!.Code.ShouldBe("outbox-not-dead-lettered");
        Queued().Retry(_clock).Error!.Code.ShouldBe("outbox-not-dead-lettered");
    }

    [Fact]
    public void Repeated_crashed_claims_dead_letter_the_email_once_MaxAttempts_is_reached()
    {
        var item = Queued();
        for (var attempt = 0; attempt < OutboxRetryPolicy.MaxAttempts; attempt++)
        {
            item.Claim("crashy", Lease, _clock).IsSuccess.ShouldBeTrue();
            _clock.Advance(Lease);
        }

        var result = item.Claim("crashy", Lease, _clock);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("outbox-dead-lettered");
        item.Status.ShouldBe(OutboxStatus.DeadLettered);
        item.Attempts.ShouldBe(OutboxRetryPolicy.MaxAttempts);
        item.LastError.ShouldBe("worker lease expired");
        item.ClaimedBy.ShouldBeNull();
        item.LockedUntil.ShouldBeNull();
    }

    [Fact]
    public void A_restored_Sending_item_without_a_lock_time_is_claimable()
    {
        var item = EmailOutboxItem.Restore(
            Guid.NewGuid(), "K", "a@example.com", "{}", null, null, OutboxStatus.Sending, 1, _clock.GetUtcNow(), "w", null, null, _clock.GetUtcNow(), null);

        item.IsClaimable(_clock).ShouldBeTrue();
        item.Claim("w2", Lease, _clock).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_lease_of_zero_or_less_is_rejected(int seconds)
    {
        var item = Queued();

        item.Claim("w", TimeSpan.FromSeconds(seconds), _clock).Error!.Code.ShouldBe("lease-invalid");
        item.Status.ShouldBe(OutboxStatus.Pending);
    }

    [Fact]
    public void Claim_with_a_blank_worker_id_is_rejected()
    {
        Queued().Claim("  ", Lease, _clock).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Sent_and_dead_lettered_items_cannot_be_claimed()
    {
        var sent = Claimed();
        sent.MarkSent("worker-1", _clock).IsSuccess.ShouldBeTrue();
        var dead = DeadLetter();

        _clock.Advance(OutboxRetryPolicy.MaxDelay);
        sent.Claim("w", Lease, _clock).Error!.Code.ShouldBe("outbox-not-claimable");
        dead.Claim("w", Lease, _clock).Error!.Code.ShouldBe("outbox-not-claimable");
    }

    [Fact]
    public void A_worker_whose_lease_expired_cannot_complete_or_fail_the_item_another_worker_reclaimed()
    {
        var item = Claimed();
        _clock.Advance(Lease);
        item.Claim("worker-2", Lease, _clock).IsSuccess.ShouldBeTrue();
        var lockedUntil = item.LockedUntil;

        item.MarkFailed("worker-1", "late", _clock).Error!.Code.ShouldBe("outbox-not-claim-owner");
        item.MarkSent("worker-1", _clock).Error!.Code.ShouldBe("outbox-not-claim-owner");

        item.Status.ShouldBe(OutboxStatus.Sending);
        item.ClaimedBy.ShouldBe("worker-2");
        item.LockedUntil.ShouldBe(lockedUntil);
        item.Attempts.ShouldBe(2);
        item.LastError.ShouldBeNull();
        item.SentAt.ShouldBeNull();
    }

    [Fact]
    public void A_sent_item_cannot_be_marked_sent_or_failed_again()
    {
        var item = Claimed();
        item.MarkSent("worker-1", _clock).IsSuccess.ShouldBeTrue();

        item.MarkSent("worker-1", _clock).IsFailure.ShouldBeTrue();
        item.MarkFailed("worker-1", "x", _clock).IsFailure.ShouldBeTrue();
        item.Status.ShouldBe(OutboxStatus.Sent);
    }

    [Fact]
    public void Stored_times_are_whole_microseconds()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero).AddTicks(3));
        var item = EmailOutboxItem.Enqueue("K", "a@example.com", null, null, null, clock).Value;
        (item.CreatedAt.Ticks % 10).ShouldBe(0);
        (item.NextAttemptAt.Ticks % 10).ShouldBe(0);

        item.Claim("w", Lease, clock).IsSuccess.ShouldBeTrue();
        (item.LockedUntil!.Value.Ticks % 10).ShouldBe(0);

        item.MarkSent("w", clock).IsSuccess.ShouldBeTrue();
        (item.SentAt!.Value.Ticks % 10).ShouldBe(0);
    }

    private EmailOutboxItem DeadLetter()
    {
        var item = Queued();
        for (var attempt = 0; attempt < OutboxRetryPolicy.MaxAttempts; attempt++)
        {
            _clock.Advance(OutboxRetryPolicy.MaxDelay);
            item.Claim("w", Lease, _clock).IsSuccess.ShouldBeTrue();
            item.MarkFailed("w", "boom", _clock);
        }

        item.Status.ShouldBe(OutboxStatus.DeadLettered);
        return item;
    }

    [Fact]
    public void Marking_sent_or_failed_without_a_worker_id_is_not_owner_even_when_nobody_claimed_the_item()
    {
        var unowned = EmailOutboxItem.Restore(
            Guid.NewGuid(), "K", "a@example.com", "{}", null, null, OutboxStatus.Sending, 1, _clock.GetUtcNow(), null, _clock.GetUtcNow().AddMinutes(5), null, _clock.GetUtcNow(), null);

        unowned.MarkSent(null, _clock).Error!.Code.ShouldBe("outbox-not-claim-owner");
        unowned.MarkFailed(null, "x", _clock).Error!.Code.ShouldBe("outbox-not-claim-owner");
        unowned.Status.ShouldBe(OutboxStatus.Sending);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_worker_id_is_never_the_claim_owner(string? workerId)
    {
        var item = Claimed();

        item.MarkSent(workerId, _clock).Error!.Code.ShouldBe("outbox-not-claim-owner");
        item.MarkFailed(workerId, "x", _clock).Error!.Code.ShouldBe("outbox-not-claim-owner");
        item.Status.ShouldBe(OutboxStatus.Sending);
        item.ClaimedBy.ShouldBe("worker-1");
    }

    private EmailOutboxItem ExpiredFifthAttempt(string? lastError) =>
        EmailOutboxItem.Restore(
            Guid.NewGuid(), "K", "a@example.com", "{}", null, null, OutboxStatus.Sending, OutboxRetryPolicy.MaxAttempts, _clock.GetUtcNow(), "crashy",
            _clock.GetUtcNow().AddMinutes(-1), lastError, _clock.GetUtcNow(), null);

    [Fact]
    public void Dead_lettering_on_lease_expiry_keeps_the_earlier_error()
    {
        var item = ExpiredFifthAttempt("smtp 550 mailbox full");

        item.Claim("w", Lease, _clock).Error!.Code.ShouldBe("outbox-dead-lettered");

        item.Status.ShouldBe(OutboxStatus.DeadLettered);
        item.LastError.ShouldBe("smtp 550 mailbox full; worker lease expired");
    }

    [Fact]
    public void Dead_lettering_on_lease_expiry_caps_the_combined_error_and_keeps_the_new_reason()
    {
        var item = ExpiredFifthAttempt(new string('e', DomainLimits.ErrorMaxLength));

        item.Claim("w", Lease, _clock);

        item.LastError!.Length.ShouldBe(DomainLimits.ErrorMaxLength);
        item.LastError.ShouldEndWith("; worker lease expired");
    }

    [Fact]
    public void A_discarded_item_cannot_be_claimed_even_when_it_is_old_and_due()
    {
        var item = DeadLetter();
        item.Discard().IsSuccess.ShouldBeTrue();
        _clock.Advance(OutboxRetryPolicy.MaxDelay * 24);

        item.IsClaimable(_clock).ShouldBeFalse();
        item.Claim("w", Lease, _clock).Error!.Code.ShouldBe("outbox-not-claimable");
        item.Status.ShouldBe(OutboxStatus.Discarded);
    }
}

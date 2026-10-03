using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Outbox;

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
        item.MarkFailed("smtp down", _clock);

        item.IsClaimable(_clock).ShouldBeFalse();
        _clock.Advance(OutboxRetryPolicy.BaseDelay);
        item.IsClaimable(_clock).ShouldBeTrue();
    }

    [Fact]
    public void Marking_sent_records_the_time_and_releases_the_claim()
    {
        var item = Claimed();
        _clock.Advance(TimeSpan.FromSeconds(2));

        item.MarkSent(_clock).IsSuccess.ShouldBeTrue();

        item.Status.ShouldBe(OutboxStatus.Sent);
        item.SentAt.ShouldBe(_clock.GetUtcNow());
        item.ClaimedBy.ShouldBeNull();
        item.LockedUntil.ShouldBeNull();
    }

    [Fact]
    public void Only_a_claimed_email_can_be_marked_sent_or_failed()
    {
        Queued().MarkSent(_clock).Error!.Code.ShouldBe("outbox-not-sending");
        Queued().MarkFailed("x", _clock).Error!.Code.ShouldBe("outbox-not-sending");
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
            item.MarkFailed("boom", _clock);

            item.Status.ShouldBe(OutboxStatus.Pending);
            item.NextAttemptAt.ShouldBe(failedAt.AddMinutes(minutes));
            _clock.Advance(TimeSpan.FromMinutes(minutes));
        }

        item.Claim("w", Lease, _clock);
        item.MarkFailed("final failure", _clock);

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

        item.MarkFailed(new string('x', 5000), _clock);

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

    private EmailOutboxItem DeadLetter()
    {
        var item = Queued();
        for (var attempt = 0; attempt < OutboxRetryPolicy.MaxAttempts; attempt++)
        {
            _clock.Advance(OutboxRetryPolicy.MaxDelay);
            item.Claim("w", Lease, _clock).IsSuccess.ShouldBeTrue();
            item.MarkFailed("boom", _clock);
        }

        item.Status.ShouldBe(OutboxStatus.DeadLettered);
        return item;
    }
}

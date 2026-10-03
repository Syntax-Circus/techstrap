using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Tickets;

public sealed class TicketAccessTokenTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    private TicketAccessToken NewToken() => TicketAccessToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "hash-1", _clock).Value;

    [Fact]
    public void A_new_token_expires_ninety_days_from_issue()
    {
        var token = NewToken();

        token.ExpiresAt.ShouldBe(_clock.GetUtcNow().AddDays(90));
        token.IsValid(_clock).ShouldBeTrue();
        token.TokenHash.ShouldBe("hash-1");
    }

    [Fact]
    public void Using_a_valid_token_slides_the_expiry_from_now()
    {
        var token = NewToken();
        _clock.Advance(TimeSpan.FromDays(40));

        token.RecordUse(_clock).IsSuccess.ShouldBeTrue();

        token.LastUsedAt.ShouldBe(_clock.GetUtcNow());
        token.ExpiresAt.ShouldBe(_clock.GetUtcNow().AddDays(90));
    }

    [Fact]
    public void A_token_expires_exactly_at_its_expiry_instant()
    {
        var token = NewToken();

        _clock.Advance(TimeSpan.FromDays(90) - TimeSpan.FromTicks(1));
        token.IsExpired(_clock).ShouldBeFalse();
        _clock.Advance(TimeSpan.FromTicks(1));
        token.IsExpired(_clock).ShouldBeTrue();
    }

    [Fact]
    public void An_expired_token_cannot_be_used_and_does_not_slide()
    {
        var token = NewToken();
        var expiry = token.ExpiresAt;
        _clock.Advance(TimeSpan.FromDays(91));

        var result = token.RecordUse(_clock);

        result.Error!.Kind.ShouldBe(DomainErrorKind.NotFound);
        token.ExpiresAt.ShouldBe(expiry);
        token.LastUsedAt.ShouldBeNull();
    }

    [Fact]
    public void A_revoked_token_is_invalid_and_cannot_be_used()
    {
        var token = NewToken();

        token.Revoke(_clock);

        token.IsValid(_clock).ShouldBeFalse();
        token.RecordUse(_clock).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_revocation_time()
    {
        var token = NewToken();
        var first = _clock.GetUtcNow();
        token.Revoke(_clock);
        _clock.Advance(TimeSpan.FromHours(2));

        token.Revoke(_clock);

        token.RevokedAt.ShouldBe(first);
    }

    [Fact]
    public void A_token_needs_a_hash()
    {
        TicketAccessToken.Issue(Guid.NewGuid(), Guid.NewGuid(), " ", _clock).Error!.Code.ShouldBe("token-hash-required");
    }
}

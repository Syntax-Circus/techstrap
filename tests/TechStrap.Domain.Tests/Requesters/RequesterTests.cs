using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Requesters;

namespace TechStrap.Domain.Tests.Requesters;

public sealed class RequesterTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("Ann@Example.COM", "ann@example.com")]
    [InlineData("  ann@example.com  ", "ann@example.com")]
    public void Email_is_trimmed_and_lower_cased(string input, string expected)
    {
        Requester.Create(input, "Ann", null, _clock).Value.Email.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ann")]
    [InlineData("ann@")]
    [InlineData("@example.com")]
    [InlineData("ann smith@example.com")]
    [InlineData("ann@example")]
    public void An_invalid_email_is_rejected(string input)
    {
        Requester.Create(input, "Ann", null, _clock).Error!.Code.ShouldBe("email-invalid");
    }

    [Fact]
    public void Name_and_external_ref_are_optional_and_blank_becomes_null()
    {
        var requester = Requester.Create("ann@example.com", "  ", " ", _clock).Value;

        requester.Name.ShouldBeNull();
        requester.ExternalUserRef.ShouldBeNull();
        requester.IsErased.ShouldBeFalse();
    }

    [Fact]
    public void Erasing_anonymises_the_requester_and_is_idempotent()
    {
        var requester = Requester.Create("ann@example.com", "Ann", "user-7", _clock).Value;
        var erasedAt = _clock.GetUtcNow();

        requester.Erase(_clock);
        _clock.Advance(TimeSpan.FromDays(1));
        requester.Erase(_clock);

        requester.Email.ShouldBe($"erased-{requester.Id}@invalid");
        requester.Name.ShouldBeNull();
        requester.ExternalUserRef.ShouldBeNull();
        requester.ErasedAt.ShouldBe(erasedAt);
        requester.IsErased.ShouldBeTrue();
    }

    [Fact]
    public void An_erased_requester_cannot_be_edited()
    {
        var requester = Requester.Create("ann@example.com", "Ann", null, _clock).Value;
        requester.Erase(_clock);

        requester.UpdateProfile("Ann", null).Error!.Kind.ShouldBe(DomainErrorKind.Conflict);
    }

    [Fact]
    public void The_profile_can_change_before_erasure()
    {
        var requester = Requester.Create("ann@example.com", null, null, _clock).Value;

        requester.UpdateProfile("Ann Lee", "user-9").IsSuccess.ShouldBeTrue();

        requester.Name.ShouldBe("Ann Lee");
        requester.ExternalUserRef.ShouldBe("user-9");
    }
}

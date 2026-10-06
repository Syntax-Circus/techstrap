using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// The protected <c>?ref=</c> value of the received page, without a host: it round trips a ticket number, expires after ten minutes (the clock that protects it is injected; the check uses the real one), refuses
/// a tampered value, a value made under another key ring or another purpose, a value that is too long and a payload that is not a ticket number. The time-limited protector is in the shared framework.
/// </summary>
public sealed class ReceivedReferenceTests
{
    private static IDataProtectionProvider Provider() => new EphemeralDataProtectionProvider();

    private static ReceivedReference Reference(IDataProtectionProvider? provider = null, TimeProvider? clock = null) => new(provider ?? Provider(), clock ?? TimeProvider.System);

    [Fact]
    public void A_ticket_number_round_trips()
    {
        var reference = Reference();

        reference.TryUnprotect(reference.Protect("PAP-42"), out var number).ShouldBeTrue();

        number.ShouldBe("PAP-42");
    }

    [Fact]
    public void The_protected_value_is_url_safe_and_does_not_contain_the_number()
    {
        var value = Reference().Protect("PAP-42");

        value.ShouldNotContain("PAP-42");
        value.ShouldMatch("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void The_lifetime_is_ten_minutes()
    {
        ReceivedReference.Lifetime.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void A_value_made_with_an_expiry_already_past_is_refused()
    {
        // Unprotect checks the real clock, so the protecting clock runs an hour behind it: the value expired 50 minutes ago.
        var reference = Reference(clock: new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1)));

        reference.TryUnprotect(reference.Protect("PAP-42"), out var number).ShouldBeFalse();

        number.ShouldBeEmpty();
    }

    [Fact]
    public void A_value_inside_its_ten_minutes_is_accepted()
    {
        var reference = Reference(clock: new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-5)));

        reference.TryUnprotect(reference.Protect("PAP-42"), out _).ShouldBeTrue();
    }

    [Fact]
    public void A_tampered_value_is_refused()
    {
        var reference = Reference();
        var value = reference.Protect("PAP-42");
        var tampered = value[..10] + (value[10] == 'A' ? 'B' : 'A') + value[11..];

        reference.TryUnprotect(tampered, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_value_from_another_key_ring_is_refused()
    {
        var made = Reference(Provider()).Protect("PAP-42");

        Reference(Provider()).TryUnprotect(made, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_value_protected_for_the_features_own_purpose_by_the_same_key_ring_is_accepted()
    {
        var provider = Provider();
        var made = provider.CreateProtector(ReceivedReference.Purpose).ToTimeLimitedDataProtector().Protect("PAP-42", DateTimeOffset.UtcNow.AddMinutes(10));

        Reference(provider).TryUnprotect(made, out var number).ShouldBeTrue();

        number.ShouldBe("PAP-42");
    }

    [Fact]
    public void A_value_protected_for_another_purpose_is_refused()
    {
        var provider = Provider();
        var otherPurpose = provider.CreateProtector("Some.Other.Purpose").ToTimeLimitedDataProtector().Protect("PAP-42", DateTimeOffset.UtcNow.AddMinutes(10));

        Reference(provider).TryUnprotect(otherPurpose, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PAP-42")]
    [InlineData("not base64 !!!")]
    [InlineData("AAAA")]
    public void Garbage_is_refused_without_throwing(string? value)
    {
        Reference().TryUnprotect(value, out var number).ShouldBeFalse();

        number.ShouldBeEmpty();
    }

    [Fact]
    public void A_value_over_the_length_limit_is_refused_before_it_is_unprotected()
    {
        Reference().TryUnprotect(new string('A', ReceivedReference.MaxReferenceLength + 1), out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("<script>")]
    [InlineData("-leading-dash")]
    [InlineData("PAP-42\r\nSet-Cookie: x=1")]
    public void An_authentic_payload_that_is_not_a_ticket_number_is_refused(string payload)
    {
        var reference = Reference();

        reference.TryUnprotect(reference.Protect(payload), out var number).ShouldBeFalse();

        number.ShouldBeEmpty();
    }

    [Fact]
    public void The_purpose_names_the_feature_and_its_version()
    {
        ReceivedReference.Purpose.ShouldBe("TechStrap.Portal.ContactReceived.v1");
    }
}

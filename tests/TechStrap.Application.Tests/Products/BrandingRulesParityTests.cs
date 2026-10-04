using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Contracts.Branding;
using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Products;

/// <summary>
/// The Admin editor checks colour and logo with Contracts <see cref="BrandingRules"/> before it submits; the API checks again with the Domain guard. Domain
/// cannot reference Contracts, so these tests pin the two to the same answers (Review Focus 4: an unsafe logo URL must be refused by both).
/// </summary>
public sealed class BrandingRulesParityTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));

    public static TheoryData<string> Colours() =>
    [
        "#000000", "#FFFFFF", "#1f6feb", "#1F6FEB", "#aAbBcC", "#12345", "#1234567", "1F6FEB", "#GGGGGG", "red", "", "#", "# 12345", "#12 456",
        "#1F6FEB ", " #1F6FEB", "#1F6FEB\n", "rgb(1,2,3)", "#1F6FEBFF", "\uFF03123456",
    ];

    public static TheoryData<string> Logos() =>
    [
        "", "   ", "https://cdn.orbitly.example/logo.png", "HTTPS://CDN.ORBITLY.EXAMPLE/Logo.png", "  https://cdn.orbitly.example/logo.png  ",
        "https://cdn.orbitly.example:8443/a/b.svg?v=2#x", "http://cdn.orbitly.example/logo.png", "http://localhost/logo.png", "http://LOCALHOST:5080/logo.png",
        "http://127.0.0.1/logo.png", "http://127.0.0.1:8080/logo.png", "http://localhost.evil.example/logo.png", "http://[::1]/logo.png", "http://192.168.0.1/logo.png",
        "javascript:alert(1)", "JAVASCRIPT:alert(1)", "data:image/png;base64,AAAA", "vbscript:x", "file:///etc/passwd", "ftp://cdn.orbitly.example/logo.png",
        "/logo.svg", "logo.svg", "../logo.svg", "//cdn.orbitly.example/logo.png", "https://", "https:///logo.png", "https:logo.png",
        "https://user:secret@cdn.orbitly.example/logo.png", "https://cdn.orbitly.example/lo go.png", "https://cdn.orbitly.example/lo\ngo.png",
        "https://cdn.orbitly.example/\u0001.png", "https://m\u00FCnchen.example/logo.png",
    ];

    [Theory]
    [MemberData(nameof(Colours))]
    public void The_colour_pattern_and_the_domain_guard_accept_and_reject_the_same_values(string sample)
    {
        // Both sides work on the trimmed value: the Domain trims before it matches, and the Admin trims before it validates.
        var byPattern = Regex.IsMatch(sample.Trim(), BrandingRules.ColourPattern);

        ProductBranding.Create("Orbitly", null, sample, null, null).IsSuccess.ShouldBe(byPattern, $"product accent '{sample}'");
        Tag.Create("bug", "Bug", sample, Clock).IsSuccess.ShouldBe(byPattern, $"tag colour '{sample}'");
    }

    [Fact]
    public void The_colour_pattern_is_the_documented_rrggbb_shape()
    {
        BrandingRules.ColourPattern.ShouldBe("^#[0-9A-Fa-f]{6}$");
    }

    [Theory]
    [MemberData(nameof(Logos))]
    public void The_logo_rule_and_the_domain_guard_accept_and_reject_the_same_values(string sample)
    {
        ProductBranding.Create("Orbitly", sample, null, null, null).IsSuccess.ShouldBe(BrandingRules.IsAcceptableLogoUrl(sample), $"logo '{sample}'");
    }

    [Fact]
    public void The_logo_rule_and_the_domain_guard_agree_at_the_length_limit()
    {
        var prefix = "https://cdn.orbitly.example/";
        var atLimit = prefix + new string('a', BrandingRules.LogoUrlMaxLength - prefix.Length);
        var over = atLimit + "a";

        BrandingRules.LogoUrlMaxLength.ShouldBe(DomainLimits.UrlMaxLength);
        BrandingRules.IsAcceptableLogoUrl(atLimit).ShouldBeTrue();
        BrandingRules.IsAcceptableLogoUrl(over).ShouldBeFalse();
        ProductBranding.Create("Orbitly", atLimit, null, null, null).IsSuccess.ShouldBeTrue();
        ProductBranding.Create("Orbitly", over, null, null, null).IsSuccess.ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://cdn.orbitly.example/logo.png", true)]
    [InlineData("http://localhost:5080/logo.png", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=", false)]
    [InlineData("/logo.svg", false)]
    [InlineData("http://cdn.orbitly.example/logo.png", false)]
    public void The_logo_rule_answers_the_documented_cases(string? value, bool expected)
    {
        BrandingRules.IsAcceptableLogoUrl(value).ShouldBe(expected);
    }
}

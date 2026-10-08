using TechStrap.Contracts.Products;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Tests.Products;

/// <summary>
/// The Admin editor checks a portal hostname with Contracts <see cref="ProductHostRules"/> before it submits; the API checks again with the Domain
/// <see cref="HostNameShape"/>. Domain cannot be referenced from the Admin, so these tests pin the two copies to the same answers (D-050).
/// </summary>
public sealed class ProductHostRulesParityTests
{
    public static TheoryData<string> Hosts() =>
    [
        "support.example.com", "Support.DragonPoop.COM", "  support.example.com  ", "a.b", "a-b.example.com", "-a.example.com", "a-.example.com",
        "example", "", "   ", "https://support.example.com", "support.example.com:8443", "support.example.com/path", "user@support.example.com",
        "support..example.com", ".example.com", "example.com.", "sup port.example.com", "support_1.example.com", "m\u00fcnchen.example.com",
        "\u212a.example.com", "1.2.3.4", "example.123", "example.1a", new string('a', 63) + ".com", new string('a', 64) + ".com",
        Padded(253), Padded(254),
    ];

    // A name of exactly the given length made of 49-character labels separated by dots, ending in "com".
    private static string Padded(int length)
    {
        var label = new string('a', 49);
        var name = string.Empty;
        while (name.Length + label.Length + 1 + 3 < length)
        {
            name += label + ".";
        }

        var fill = length - name.Length - 4;
        return name + new string('b', fill) + ".com";
    }

    [Fact]
    public void The_length_limit_matches_the_domain_limit()
    {
        ProductHostRules.HostNameMaxLength.ShouldBe(DomainLimits.HostNameMaxLength);
        ProductHostRules.HostNameMaxLength.ShouldBe(253);
    }

    [Fact]
    public void The_padded_samples_have_the_lengths_the_boundary_cases_need()
    {
        Padded(253).Length.ShouldBe(253);
        Padded(254).Length.ShouldBe(254);
        HostNameShape.TryNormalize(Padded(253), out _).ShouldBeTrue();
        HostNameShape.TryNormalize(Padded(254), out _).ShouldBeFalse();
    }

    [Fact]
    public void A_null_input_is_blank_for_both_rules()
    {
        HostNameShape.TryNormalize(null, out var domainHost).ShouldBeTrue();
        ProductHostRules.TryNormalize(null, out var contractsHost).ShouldBeTrue();
        domainHost.ShouldBeNull();
        contractsHost.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Both_rules_give_the_same_answer_and_the_same_normalised_host(string sample)
    {
        var domain = HostNameShape.TryNormalize(sample, out var domainHost);
        var contracts = ProductHostRules.TryNormalize(sample, out var contractsHost);

        contracts.ShouldBe(domain, $"validity of '{sample}'");
        contractsHost.ShouldBe(domainHost, $"normalised form of '{sample}'");
    }
}

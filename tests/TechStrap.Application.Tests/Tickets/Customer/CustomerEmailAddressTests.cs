using Microsoft.Extensions.Time.Testing;
using TechStrap.Application.Tickets.Customer;
using TechStrap.Domain.Requesters;

namespace TechStrap.Application.Tests.Tickets.Customer;

public sealed class CustomerEmailAddressTests
{
    [Theory]
    [InlineData(" Ann@Example.com ", "ann@example.com")]
    public void Valid_addresses_are_normalised(string input, string expected)
    {
        CustomerEmailAddress.TryNormalize(input, out var email).ShouldBeTrue();
        email.ShouldBe(expected);
    }

    [Theory]
    [InlineData("ann")]
    [InlineData("ann@")]
    [InlineData("a b@c.d")]
    [InlineData(null)]
    public void Malformed_addresses_are_rejected(string? input) =>
        CustomerEmailAddress.TryNormalize(input, out _).ShouldBeFalse();

    [Fact]
    public void The_rule_matches_the_domain()
    {
        var clock = new FakeTimeProvider();
        var addresses = new List<string?>
        {
            "ann@example.com", " Ann@Example.com ", "ann", "ann@", "@example.com", "ann@example", "ann@@example.com", "a b@c.d",
            "a@b.c", "a@b.c.d", "ann@.com", "ann@example.", "", "   ", null, "ann@exa mple.com", "ann+tag@example.com",
            new string('a', 308) + "@example.com",
            new string('a', 309) + "@example.com",
            new string('a', 64) + "@" + new string('b', 250) + ".com",
            "ANN@EXAMPLE.COM\t",
        };

        foreach (var address in addresses)
        {
            var domain = Requester.Create(address, "Ann", null, clock);
            var ours = CustomerEmailAddress.TryNormalize(address, out var email);
            ours.ShouldBe(domain.IsSuccess, $"address: {address}");
            if (ours)
            {
                email.ShouldBe(domain.Value.Email);
            }
        }
    }
}

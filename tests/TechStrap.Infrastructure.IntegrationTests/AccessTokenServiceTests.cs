using Microsoft.Extensions.Time.Testing;
using TechStrap.Infrastructure.Security;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class AccessTokenServiceTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));

    private AccessTokenService Service() => new(_clock);

    [Fact]
    public void Ten_thousand_tokens_never_collide()
    {
        var service = Service();

        Enumerable.Range(0, 10_000).Select(_ => service.Issue(Guid.CreateVersion7(), Guid.CreateVersion7()).Value.PlaintextToken)
            .Distinct().Count().ShouldBe(10_000);
    }

    [Fact]
    public void A_token_is_256_bits_of_base64url_and_only_its_hash_is_on_the_entity()
    {
        var issued = Service().Issue(Guid.CreateVersion7(), Guid.CreateVersion7()).Value;

        issued.PlaintextToken.ShouldMatch("^[A-Za-z0-9_-]{43}$");
        issued.Token.TokenHash.ShouldBe(Service().Hash(issued.PlaintextToken));
        issued.Token.TokenHash.ShouldStartWith("sha256:");
        issued.Token.TokenHash.ShouldNotContain(issued.PlaintextToken);
    }

    [Fact]
    public void The_hash_is_the_documented_sha256() =>
        Service().Hash("abc").ShouldBe("sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("abc"u8)));

    [Fact]
    public void A_new_token_expires_after_the_sliding_lifetime() =>
        Service().Issue(Guid.CreateVersion7(), Guid.CreateVersion7()).Value.Token.ExpiresAt.ShouldBe(_clock.GetUtcNow() + Domain.Tickets.TicketAccessToken.Lifetime);
}

using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Products;

namespace TechStrap.Domain.Tests.Products;

public sealed class ProductApiKeyTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly Guid _productId = Guid.NewGuid();

    private ProductApiKey NewKey(ApiKeyKind kind = ApiKeyKind.Trusted) =>
        ProductApiKey.Create(_productId, kind, "tsk_abcd", "hash-1", "Production", _clock).Value;

    [Fact]
    public void A_new_key_is_active_and_keeps_only_prefix_and_hash()
    {
        var key = NewKey();

        key.IsRevoked.ShouldBeFalse();
        key.KeyPrefix.ShouldBe("tsk_abcd");
        key.KeyHash.ShouldBe("hash-1");
        key.CreatedAt.ShouldBe(_clock.GetUtcNow());
        key.ProductId.ShouldBe(_productId);
    }

    [Theory]
    [InlineData(ApiKeyKind.Trusted, true)]
    [InlineData(ApiKeyKind.Public, false)]
    public void Only_a_trusted_key_trusts_metadata(ApiKeyKind kind, bool trusts)
    {
        NewKey(kind).TrustsMetadata.ShouldBe(trusts);
    }

    [Theory]
    [InlineData("", "hash", "key-prefix-required")]
    [InlineData("abc", "hash", "key-prefix-too-short")]
    [InlineData("tsk_abcd", "", "key-hash-required")]
    public void A_missing_or_short_prefix_or_hash_is_rejected(string prefix, string hash, string code)
    {
        ProductApiKey.Create(_productId, ApiKeyKind.Public, prefix, hash, null, _clock).Error!.Code.ShouldBe(code);
    }

    [Fact]
    public void Revoking_sets_the_time_once_and_a_second_revoke_keeps_the_first_time()
    {
        var key = NewKey();
        var firstRevocation = _clock.GetUtcNow();
        key.Revoke(_clock);

        _clock.Advance(TimeSpan.FromHours(1));
        key.Revoke(_clock);

        key.IsRevoked.ShouldBeTrue();
        key.RevokedAt.ShouldBe(firstRevocation);
    }

    [Fact]
    public void A_revoked_key_cannot_record_use()
    {
        var key = NewKey();
        key.Revoke(_clock);

        var result = key.RecordUse(_clock);

        result.Error!.Kind.ShouldBe(DomainErrorKind.Conflict);
        key.LastUsedAt.ShouldBeNull();
    }

    [Fact]
    public void An_active_key_records_its_last_use()
    {
        var key = NewKey();
        _clock.Advance(TimeSpan.FromMinutes(5));

        key.RecordUse(_clock).IsSuccess.ShouldBeTrue();

        key.LastUsedAt.ShouldBe(_clock.GetUtcNow());
    }
}

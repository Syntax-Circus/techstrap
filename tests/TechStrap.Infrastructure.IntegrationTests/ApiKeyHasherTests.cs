using TechStrap.Application.ApiKeys;
using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Security;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class ApiKeyHasherTests
{
    private readonly ApiKeyHasher _hasher = new();

    [Theory]
    [InlineData(ApiKeyKind.Trusted, "tsk_")]
    [InlineData(ApiKeyKind.Public, "tsp_")]
    public void A_generated_key_has_the_kind_prefix_a_256_bit_secret_and_a_matching_hash(ApiKeyKind kind, string prefix)
    {
        var key = _hasher.Generate(kind);

        key.PlaintextKey.ShouldStartWith(prefix);
        key.PlaintextKey.Length.ShouldBe(prefix.Length + ApiKeyFormat.SecretLength);
        key.PlaintextKey[prefix.Length..].ShouldMatch("^[A-Za-z0-9_-]{43}$");
        key.KeyPrefix.ShouldBe(key.PlaintextKey[..ApiKeyFormat.StoredPrefixLength]);
        key.KeyHash.ShouldBe(_hasher.Hash(key.PlaintextKey));
        key.KeyHash.ShouldStartWith("sha256:");
        key.KeyHash.ShouldNotContain(key.PlaintextKey[prefix.Length..]);
    }

    [Fact]
    public void A_thousand_generated_keys_are_all_different()
    {
        var keys = Enumerable.Range(0, 1000).Select(_ => _hasher.Generate(ApiKeyKind.Trusted)).ToList();

        keys.Select(k => k.PlaintextKey).Distinct().Count().ShouldBe(1000);
        keys.Select(k => k.KeyHash).Distinct().Count().ShouldBe(1000);
    }

    [Fact]
    public void Verify_accepts_only_the_exact_key()
    {
        var key = _hasher.Generate(ApiKeyKind.Public);

        _hasher.Verify(key.PlaintextKey, key.KeyHash).ShouldBeTrue();
        _hasher.Verify(key.PlaintextKey + "x", key.KeyHash).ShouldBeFalse();
        _hasher.Verify(key.PlaintextKey[..^1], key.KeyHash).ShouldBeFalse();
        _hasher.Verify(string.Empty, key.KeyHash).ShouldBeFalse();
        _hasher.Verify(key.PlaintextKey, "sha256:00").ShouldBeFalse();
    }

    [Fact]
    public void The_hash_is_the_documented_sha256_of_the_key() =>
        _hasher.Hash("tsk_abc").ShouldBe("sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("tsk_abc"u8)));

    [Fact]
    public void Verify_compares_in_constant_time()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TechStrap.Infrastructure", "Security", "ApiKeyHasher.cs"));

        source.ShouldContain("CryptographicOperations.FixedTimeEquals");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

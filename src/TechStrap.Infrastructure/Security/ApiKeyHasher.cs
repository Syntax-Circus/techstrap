using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using TechStrap.Application.ApiKeys;
using TechStrap.Domain.Products;

namespace TechStrap.Infrastructure.Security;

internal sealed class ApiKeyHasher : IApiKeyHasher
{
    public GeneratedApiKey Generate(ApiKeyKind kind)
    {
        var plaintext = ApiKeyFormat.KindPrefix(kind) + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ApiKeyFormat.SecretBytes));
        return new GeneratedApiKey(plaintext, plaintext[..ApiKeyFormat.StoredPrefixLength], Hash(plaintext));
    }

    public string Hash(string plaintextKey) =>
        ApiKeyFormat.HashScheme + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey)));

    public bool Verify(string plaintextKey, string keyHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(plaintextKey)), Encoding.UTF8.GetBytes(keyHash));
}

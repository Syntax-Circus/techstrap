using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.ApiKeys;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Products;

namespace TechStrap.Api.Tests.Intake;

internal sealed record IntakeSeed(
    Product Orbitly, Product Paperplane, Product Dormant,
    string OrbitlyTrusted, string OrbitlyPublic, string OrbitlyRevoked, string PaperplaneTrusted, string DormantTrusted);

/// <summary>Seeds products and keys through the real repositories and hasher. Plaintext keys exist only in test memory.</summary>
internal static class IntakeTestData
{
    public static async Task<IntakeSeed> SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var clock = provider.GetRequiredService<TimeProvider>();
        var hasher = provider.GetRequiredService<IApiKeyHasher>();
        var products = provider.GetRequiredService<IProductRepository>();

        var orbitly = Product.Create("orbitly", "Orbitly", "ORB", null, clock).Value;
        var paperplane = Product.Create("paperplane", "Paperplane", "PPL", null, clock).Value;
        var dormant = Product.Create("dormant", "Dormant", "DRM", null, clock).Value;
        dormant.SetActive(false);

        await using var work = await provider.GetRequiredService<IUnitOfWork>().BeginAsync(cancellationToken);
        products.Add(orbitly);
        products.Add(paperplane);
        products.Add(dormant);

        string AddKey(Product product, ApiKeyKind kind, bool revoke = false)
        {
            var generated = hasher.Generate(kind);
            var key = ProductApiKey.Create(product.Id, kind, generated.KeyPrefix, generated.KeyHash, "test", clock).Value;
            if (revoke)
            {
                key.Revoke(clock);
            }

            products.AddApiKey(key);
            return generated.PlaintextKey;
        }

        var seed = new IntakeSeed(
            orbitly, paperplane, dormant,
            AddKey(orbitly, ApiKeyKind.Trusted),
            AddKey(orbitly, ApiKeyKind.Public),
            AddKey(orbitly, ApiKeyKind.Trusted, revoke: true),
            AddKey(paperplane, ApiKeyKind.Trusted),
            AddKey(dormant, ApiKeyKind.Trusted));
        (await work.CommitAsync(cancellationToken)).IsSuccess.ShouldBeTrue();
        return seed;
    }
}

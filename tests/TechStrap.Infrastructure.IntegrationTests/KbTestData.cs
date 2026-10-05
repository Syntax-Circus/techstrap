using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// A shared category for KB tests that publish articles: publishing needs a category (D-044), and the foreign key needs the row to exist.
/// </summary>
internal static class KbTestData
{
    public static readonly Guid SharedCategoryId = Guid.Parse("0199a000-0000-7000-8000-00000000c001");

    public const string SharedCategorySlug = "kb-test-general";

    /// <summary>Stages the shared category once per database; calling it again is harmless.</summary>
    public static async Task EnsureSharedCategoryAsync(PersistenceTestHost host)
    {
        await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            if (await kb.GetCategoryAsync(SharedCategoryId, TestContext.Current.CancellationToken) is null)
            {
                kb.AddCategory(KbCategory.Restore(SharedCategoryId, null, "General", SharedCategorySlug, null, 0, 0));
            }
        });
    }
}

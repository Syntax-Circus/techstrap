using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>Reads the EF model without a database, so schema rules can be asserted on the model itself.</summary>
internal static partial class ModelInspector
{
    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)*$")]
    public static partial Regex SnakeCase();

    private static readonly Lazy<IModel> CachedModel = new(CreateModel);

    /// <summary>The model is immutable and costs a full build, so it is built once per test run.</summary>
    public static IModel BuildModel() => CachedModel.Value;

    private static IModel CreateModel()
    {
        var options = new DbContextOptionsBuilder<TechStrapDbContext>();
        TechStrapDatabase.Configure(options, "Host=localhost;Database=model_only");
        using var context = new TechStrapDbContext(options.Options);
        return context.GetService<IDesignTimeModel>().Model;
    }

    public static IEnumerable<IEntityType> Entities() => BuildModel().GetEntityTypes();

    public static IEntityType Table(string name) => Entities().Single(e => e.GetTableName() == name);

    public static bool HasTable(string name) => Entities().Any(e => e.GetTableName() == name);

    public static bool HasUniqueIndex(IEntityType entity, params string[] columns) =>
        entity.GetIndexes().Any(index => index.IsUnique && index.Properties.Select(p => p.GetColumnName()).SequenceEqual(columns));
}

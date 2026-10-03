using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace TechStrap.Infrastructure.Persistence.Configurations;

internal static class ForeignKeyNaming
{
    /// <summary>
    /// Names every foreign key <c>fk_{dependent_table}_{principal_table}_{fk_columns joined by _}</c> from the real table and
    /// column names, so no constraint name leaks a persistence record class name. Call it after the configurations are applied.
    /// </summary>
    public static void ApplyTableBasedNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null)
            {
                continue;
            }

            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var principal = foreignKey.PrincipalEntityType.GetTableName()!;
                var columns = string.Join('_', foreignKey.Properties.Select(p => p.GetColumnName(store)!));
                foreignKey.SetConstraintName($"fk_{table}_{principal}_{columns}");
            }
        }
    }
}

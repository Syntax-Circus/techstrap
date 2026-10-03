using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// The schema rules that hold for every table, present and future (PHASE-03): snake_case names on every table, column, key,
/// foreign key and index (join tables included), and enums stored as text. The model tests need no database; the last test
/// checks the migrated database itself.
/// </summary>
public sealed class SchemaConventionTests(PostgresFixture postgres)
{
    private const string MigrationsHistoryTable = "__EFMigrationsHistory";

    [Fact]
    public void Every_table_column_key_foreign_key_and_index_name_in_the_model_is_snake_case()
    {
        var offenders = new List<string>();
        var entities = ModelInspector.Entities().ToList();
        entities.ShouldNotBeEmpty();
        foreach (var entity in entities)
        {
            var table = entity.GetTableName()!;
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            Check(table, $"table {table}");
            foreach (var property in entity.GetProperties())
            {
                Check(property.GetColumnName(store)!, $"{table}.{property.Name}");
            }

            foreach (var key in entity.GetKeys())
            {
                Check(key.GetName()!, $"{table} key");
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                Check(foreignKey.GetConstraintName()!, $"{table} foreign key");
            }

            foreach (var index in entity.GetIndexes())
            {
                Check(index.GetDatabaseName()!, $"{table} index");
            }
        }

        offenders.ShouldBeEmpty();

        void Check(string name, string what)
        {
            if (!ModelInspector.SnakeCase().IsMatch(name))
            {
                offenders.Add($"{what}: '{name}'");
            }
        }
    }

    [Fact]
    public void Every_enum_property_is_stored_as_text()
    {
        var offenders = ModelInspector.Entities()
            .SelectMany(e => e.GetProperties().Select(p => (Entity: e, Property: p)))
            .Where(x => (Nullable.GetUnderlyingType(x.Property.ClrType) ?? x.Property.ClrType).IsEnum)
            .Where(x => x.Property.GetProviderClrType() != typeof(string))
            .Select(x => $"{x.Entity.GetTableName()}.{x.Property.Name}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_table_and_column_in_the_migrated_database_is_snake_case_and_every_model_table_exists()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.table_name, c.column_name
            FROM information_schema.columns c
            WHERE c.table_schema = 'public' AND c.table_name <> @history
            """;
        command.Parameters.AddWithValue("history", MigrationsHistoryTable);

        var offenders = new List<string>();
        var tables = new HashSet<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(reader.GetString(0));
            if (!ModelInspector.SnakeCase().IsMatch(reader.GetString(0)) || !ModelInspector.SnakeCase().IsMatch(reader.GetString(1)))
            {
                offenders.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
            }
        }

        offenders.ShouldBeEmpty();
        tables.Order(StringComparer.Ordinal).ShouldBe(ModelInspector.Entities().Select(e => e.GetTableName()!).Order(StringComparer.Ordinal));
    }
}

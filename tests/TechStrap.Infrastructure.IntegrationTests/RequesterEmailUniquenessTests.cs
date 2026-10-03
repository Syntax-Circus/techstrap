using Npgsql;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>Pins citext plus the unique index: the database itself rejects requester emails that differ only in case.</summary>
public sealed class RequesterEmailUniquenessTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private const string UniqueViolation = "23505";

    [Fact]
    public async Task Two_requesters_whose_emails_differ_only_in_case_violate_the_unique_index()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await InsertRequesterAsync(connection, "Sam@Example.com");
        var exception = await Should.ThrowAsync<PostgresException>(() => InsertRequesterAsync(connection, "sam@example.com"));

        exception.SqlState.ShouldBe(UniqueViolation);
    }

    private static async Task InsertRequesterAsync(NpgsqlConnection connection, string email)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO requesters (id, email) VALUES (@id, @email)";
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("email", email);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}

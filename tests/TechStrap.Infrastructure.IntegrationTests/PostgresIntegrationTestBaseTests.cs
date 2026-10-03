namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class PostgresIntegrationTestBaseTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Disposing_before_initialisation_succeeded_does_not_throw()
    {
        // Simulates InitializeAsync having thrown: the per-test database was never assigned.
        var test = new Probe(postgres);

        await test.DisposeAsync();
    }

    [Fact]
    public async Task Using_the_database_before_initialisation_fails_with_a_clear_message()
    {
        var test = new Probe(postgres);

        Should.Throw<InvalidOperationException>(() => test.ReadDatabase()).Message.ShouldContain("has not been created");
        await test.DisposeAsync();
    }

    [Fact]
    public async Task After_initialisation_the_database_exists_and_is_dropped_on_dispose()
    {
        var test = new Probe(postgres);
        await test.InitializeAsync();

        var name = test.ReadDatabase().Name;
        await test.DisposeAsync();

        name.ShouldStartWith("t_");
    }

    private sealed class Probe(PostgresFixture fixture) : PostgresIntegrationTestBase(fixture)
    {
        public TestDatabase ReadDatabase() => Database;
    }
}

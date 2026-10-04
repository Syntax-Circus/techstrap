namespace TechStrap.Architecture.Tests;

public sealed class LoggingSafetyTests
{
    [Fact]
    public void No_source_or_deployment_file_enables_sensitive_logging_or_npgsql_error_detail()
    {
        var sources = LoggingSafetyRules.Sources(ProjectGraph.FindRepositoryRoot()).ToList();

        sources.ShouldContain(s => s.Path.EndsWith("TechStrapDatabase.cs", StringComparison.Ordinal), "the scan must see the Infrastructure sources");
        LoggingSafetyRules.FindViolations(sources).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("options.EnableSensitiveDataLogging();")]
    [InlineData("Host=db;Include Error Detail=true")]
    [InlineData("ConnectionStrings__TechStrap: Host=db;IncludeErrorDetail=true")]
    public void A_setting_that_leaks_data_is_flagged(string text)
    {
        LoggingSafetyRules.FindViolations([("Bad.cs", text)]).ShouldNotBeEmpty();
    }

    [Fact]
    public void Ordinary_logging_configuration_passes()
    {
        LoggingSafetyRules.FindViolations([("Ok.cs", "options.EnableDetailedErrors(); // Serilog")]).ShouldBeEmpty();
    }
}

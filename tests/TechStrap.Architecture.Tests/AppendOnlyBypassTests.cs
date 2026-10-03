namespace TechStrap.Architecture.Tests;

public sealed class AppendOnlyBypassTests
{
    [Fact]
    public void Infrastructure_never_bulk_updates_or_deletes_the_append_only_event_tables()
    {
        var sources = AppendOnlyBypassRules.InfrastructureSources(ProjectGraph.FindRepositoryRoot()).ToList();

        sources.ShouldContain(s => s.Path.EndsWith("AppendOnlyEventInterceptor.cs", StringComparison.Ordinal), "the scan must see the Infrastructure sources");
        AppendOnlyBypassRules.FindViolations(sources).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("db.Set<TicketEventRecord>().Where(e => e.Id == id).ExecuteDelete();")]
    [InlineData("await context.Set<AdminEventRecord>().ExecuteUpdateAsync(s => s.SetProperty(e => e.Payload, x));")]
    [InlineData("context.Database.ExecuteSqlRaw(\"UPDATE ticket_events SET payload = x\");")]
    [InlineData("context.Database.ExecuteSqlRaw(\"DELETE FROM admin_events\");")]
    [InlineData("context.Database.ExecuteSqlRaw(\"TRUNCATE TABLE ticket_events\");")]
    public void A_bypass_of_the_interceptor_is_flagged(string source)
    {
        AppendOnlyBypassRules.FindViolations([("Bad.cs", source)]).ShouldNotBeEmpty();
    }

    [Fact]
    public void Bulk_operations_on_other_tables_and_plain_reads_of_events_pass()
    {
        AppendOnlyBypassRules.FindViolations(
        [
            ("Ok.cs", "context.Set<TagRecord>().ExecuteDelete();"),
            ("Reads.cs", "context.Set<TicketEventRecord>().AsNoTracking().ToListAsync();"),
        ]).ShouldBeEmpty();
    }
}

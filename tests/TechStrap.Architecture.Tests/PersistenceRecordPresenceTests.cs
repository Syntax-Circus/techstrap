using TechStrap.Infrastructure;

namespace TechStrap.Architecture.Tests;

/// <summary>Keeps the record rules honest: they only mean something while Infrastructure actually declares internal records.</summary>
public sealed class PersistenceRecordPresenceTests
{
    [Fact]
    public void The_record_scan_is_not_vacuous_infrastructure_has_internal_records()
    {
        var records = typeof(InfrastructureAssemblyMarker).Assembly.GetTypes()
            .Where(t => t.Namespace == PersistenceRecordRules.RecordNamespace && !t.IsNested)
            .ToList();

        records.ShouldContain(t => t.Name == "ProductRecord");
        records.ShouldAllBe(t => t.IsNotPublic);
    }
}

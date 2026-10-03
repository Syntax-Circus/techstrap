using TechStrap.Application;
using TechStrap.Domain;
using TechStrap.Infrastructure;

namespace TechStrap.Architecture.Tests;

/// <summary>D-026: persistence records never leave Infrastructure.</summary>
public sealed class PersistenceRecordBoundaryTests
{
    private static readonly Type[] DomainTypes = typeof(EntityId).Assembly.GetTypes();
    private static readonly Type[] ApplicationTypes = typeof(ApplicationAssemblyMarker).Assembly.GetTypes();

    [Fact]
    public void No_Domain_type_declares_or_references_a_persistence_record()
    {
        PersistenceRecordRules.FindReferencesFromDomainOrApplication(DomainTypes).ShouldBeEmpty();
    }

    [Fact]
    public void No_Application_type_declares_or_references_a_persistence_record()
    {
        PersistenceRecordRules.FindReferencesFromDomainOrApplication(ApplicationTypes).ShouldBeEmpty();
    }

    [Fact]
    public void Infrastructure_records_follow_the_naming_and_visibility_convention()
    {
        PersistenceRecordRules.FindRecordDeclarationViolations(typeof(InfrastructureAssemblyMarker).Assembly).ShouldBeEmpty();
    }

    [Fact]
    public void A_type_that_does_not_mention_records_passes()
    {
        PersistenceRecordRules.FindReferencesFromDomainOrApplication([typeof(AbstractionFixtures.Innocent)]).ShouldBeEmpty();
    }

    [Fact]
    public void A_type_named_like_a_record_is_flagged()
    {
        PersistenceRecordRules.FindReferencesFromDomainOrApplication([typeof(AbstractionFixtures.WidgetRecord)])
            .ShouldContain(violation => violation.Contains("named like a persistence record", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(typeof(AbstractionFixtures.HoldsRecordInField))]
    [InlineData(typeof(AbstractionFixtures.ReturnsRecord))]
    [InlineData(typeof(AbstractionFixtures.TakesRecordInConstructor))]
    [InlineData(typeof(AbstractionFixtures.HoldsRecordsInGenericProperty))]
    public void A_type_that_mentions_a_record_is_flagged(Type bad)
    {
        PersistenceRecordRules.FindReferencesFromDomainOrApplication([bad])
            .ShouldContain(violation => violation.Contains("must not reference persistence record", StringComparison.Ordinal));
    }
}

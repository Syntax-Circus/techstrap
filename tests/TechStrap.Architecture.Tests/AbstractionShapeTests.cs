using TechStrap.Application;

namespace TechStrap.Architecture.Tests;

public sealed class AbstractionShapeTests
{
    private static readonly Type[] ApplicationTypes = typeof(ApplicationAssemblyMarker).Assembly.GetTypes();

    [Fact]
    public void Application_abstractions_expose_no_persistence_http_or_infrastructure_types_and_take_cancellation_tokens()
    {
        AbstractionRules.FindShapeViolations(ApplicationTypes).ShouldBeEmpty();
    }

    [Fact]
    public void The_scan_is_not_vacuous_it_sees_the_persistence_abstractions()
    {
        var names = ApplicationTypes.Where(AbstractionRules.IsAbstraction).Select(t => t.Name).ToList();

        names.ShouldContain("IUnitOfWork");
        names.ShouldContain("ITicketRepository");
        names.ShouldContain("IEmailOutboxStore");
        names.Count.ShouldBeGreaterThanOrEqualTo(13);
    }

    [Fact]
    public void A_clean_abstraction_passes()
    {
        AbstractionRules.FindShapeViolations([typeof(AbstractionFixtures.IGoodStore)]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(typeof(AbstractionFixtures.IExposesDbContext), "Microsoft.EntityFrameworkCore.DbContext")]
    [InlineData(typeof(AbstractionFixtures.IExposesDbSet), "Microsoft.EntityFrameworkCore.DbSet")]
    [InlineData(typeof(AbstractionFixtures.IExposesQueryable), "System.Linq.IQueryable")]
    [InlineData(typeof(AbstractionFixtures.IExposesNestedQueryable), "System.Linq.IQueryable")]
    [InlineData(typeof(AbstractionFixtures.IExposesHttpContext), "Microsoft.AspNetCore.Http.HttpContext")]
    [InlineData(typeof(AbstractionFixtures.IExposesInfrastructure), "TechStrap.Infrastructure.InfrastructureAssemblyMarker")]
    [InlineData(typeof(AbstractionFixtures.IExposesRecord), "WidgetRecord")]
    public void An_abstraction_exposing_a_forbidden_type_is_flagged(Type bad, string forbiddenFragment)
    {
        AbstractionRules.FindShapeViolations([bad]).ShouldContain(violation => violation.Contains(forbiddenFragment, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(typeof(AbstractionFixtures.IMissingCancellationToken))]
    [InlineData(typeof(AbstractionFixtures.ICancellationTokenNotLast))]
    public void An_async_method_without_a_trailing_cancellation_token_is_flagged(Type bad)
    {
        AbstractionRules.FindShapeViolations([bad]).ShouldContain(violation => violation.Contains("must take a CancellationToken", StringComparison.Ordinal));
    }
}

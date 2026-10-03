using System.Reflection;
using TechStrap.Application;
using TechStrap.Infrastructure;

namespace TechStrap.Architecture.Tests;

public sealed class HandlerConstructorDependencyTests
{
    private static readonly Type[] ApplicationTypes = typeof(ApplicationAssemblyMarker).Assembly.GetTypes();
    private static readonly Assembly InfrastructureAssembly = typeof(InfrastructureAssemblyMarker).Assembly;

    [Fact]
    public void Application_handlers_depend_only_on_approved_abstractions()
    {
        // Passes vacuously until PHASE-04 adds the first handler; the rule is already enforced.
        HandlerRules.FindConstructorDependencyViolations(ApplicationTypes, InfrastructureAssembly).ShouldBeEmpty();
    }

    [Fact]
    public void A_handler_taking_only_an_application_interface_passes()
    {
        HandlerRules.FindConstructorDependencyViolations([typeof(HandlerFixtures.GoodSampleHandler)], InfrastructureAssembly)
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData(typeof(HandlerFixtures.DbContextHandler), "Microsoft.EntityFrameworkCore.DbContext")]
    [InlineData(typeof(HandlerFixtures.DbSetHandler), "Microsoft.EntityFrameworkCore.DbSet")]
    [InlineData(typeof(HandlerFixtures.HttpContextHandler), "Microsoft.AspNetCore.Http.HttpContext")]
    [InlineData(typeof(HandlerFixtures.HttpContextAccessorHandler), "Microsoft.AspNetCore.Http.IHttpContextAccessor")]
    [InlineData(typeof(HandlerFixtures.ActionResultHandler), "Microsoft.AspNetCore.Mvc.IActionResult")]
    [InlineData(typeof(HandlerFixtures.ControllerBaseHandler), "Microsoft.AspNetCore.Mvc.ControllerBase")]
    [InlineData(typeof(HandlerFixtures.ConcreteInfrastructureHandler), "TechStrap.Infrastructure.InfrastructureAssemblyMarker")]
    [InlineData(typeof(HandlerFixtures.NestedGenericDependencyHandler), "Microsoft.EntityFrameworkCore.DbSet")]
    [InlineData(typeof(HandlerFixtures.RecordHandler), "TicketRecord")]
    [InlineData(typeof(HandlerFixtures.NestedRecordHandler), "TicketRecord")]
    public void A_handler_with_a_forbidden_dependency_is_flagged(Type badHandler, string forbiddenTypeFragment)
    {
        var violations = HandlerRules.FindConstructorDependencyViolations([badHandler], InfrastructureAssembly);

        violations.ShouldContain(violation => violation.Contains(forbiddenTypeFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void Application_handlers_take_only_approved_abstractions()
    {
        var handlers = typeof(ApplicationAssemblyMarker).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .ToList();

        handlers.Count.ShouldBeGreaterThanOrEqualTo(18);
        HandlerRules.FindUnapprovedDependencies(handlers, typeof(ApplicationAssemblyMarker).Assembly).ShouldBeEmpty();
    }

    [Fact]
    public void A_handler_taking_an_http_client_is_flagged() =>
        HandlerRules.FindUnapprovedDependencies([typeof(HandlerFixtures.HttpClientSampleHandler)], typeof(HandlerConstructorDependencyTests).Assembly)
            .ShouldHaveSingleItem().ShouldContain("HttpClient");

    [Fact]
    public void A_handler_taking_an_application_interface_and_a_clock_passes() =>
        HandlerRules.FindUnapprovedDependencies([typeof(HandlerFixtures.GoodSampleHandler)], typeof(HandlerFixtures.GoodSampleHandler).Assembly).ShouldBeEmpty();
}

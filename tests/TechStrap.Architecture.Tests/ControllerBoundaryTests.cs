using Microsoft.AspNetCore.Mvc;
using TechStrap.Application;
using TechStrap.Architecture.Tests.ControllerBoundaryFixtureTypes;

namespace TechStrap.Architecture.Tests;

public sealed class ControllerBoundaryTests
{
    private static readonly System.Reflection.Assembly FixtureAssembly = typeof(ControllerBoundaryTests).Assembly;

    [Fact]
    public void Every_api_controller_is_a_thin_policy_guarded_adapter()
    {
        var controllers = typeof(TechStrap.Api.Program).Assembly.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type)).ToList();

        controllers.Count.ShouldBeGreaterThanOrEqualTo(4);
        ControllerBoundaryRules.FindViolations(controllers, typeof(ApplicationAssemblyMarker).Assembly).ShouldBeEmpty();
    }

    [Fact]
    public void A_good_controller_passes() =>
        ControllerBoundaryRules.FindViolations([typeof(GoodBoundaryController)], FixtureAssembly).ShouldBeEmpty();

    [Theory]
    [InlineData(typeof(NoPolicyController), "Authorize")]
    [InlineData(typeof(RepositoryParameterController), "IFixtureRepository")]
    [InlineData(typeof(TwoHandlersController), "exactly one")]
    [InlineData(typeof(NoCancellationController), "CancellationToken")]
    [InlineData(typeof(ConstructorDependencyController), "constructor")]
    public void A_bad_controller_is_flagged(Type controller, string expected) =>
        ControllerBoundaryRules.FindViolations([controller], FixtureAssembly).ShouldContain(violation => violation.Contains(expected));
}

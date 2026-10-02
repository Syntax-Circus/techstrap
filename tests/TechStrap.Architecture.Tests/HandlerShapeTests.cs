using TechStrap.Application;

namespace TechStrap.Architecture.Tests;

public sealed class HandlerShapeTests
{
    private static readonly Type[] ApplicationTypes = typeof(ApplicationAssemblyMarker).Assembly.GetTypes();

    [Fact]
    public void Application_handlers_are_sealed_have_a_matching_interface_and_take_a_cancellation_token()
    {
        // Passes vacuously until PHASE-04 adds the first handler; the rule is already enforced.
        HandlerRules.FindShapeViolations(ApplicationTypes).ShouldBeEmpty();
    }

    [Fact]
    public void A_well_formed_handler_passes()
    {
        HandlerRules.FindShapeViolations([typeof(HandlerFixtures.GoodSampleHandler)]).ShouldBeEmpty();
    }

    [Fact]
    public void An_unsealed_handler_is_flagged()
    {
        HandlerRules.FindShapeViolations([typeof(HandlerFixtures.UnsealedHandler)])
            .ShouldContain("UnsealedHandler must be sealed.");
    }

    [Fact]
    public void A_handler_without_a_matching_interface_is_flagged()
    {
        HandlerRules.FindShapeViolations([typeof(HandlerFixtures.NoInterfaceHandler)])
            .ShouldContain("NoInterfaceHandler must implement INoInterfaceHandler.");
    }

    [Fact]
    public void A_handler_method_without_a_cancellation_token_is_flagged()
    {
        HandlerRules.FindShapeViolations([typeof(HandlerFixtures.NoCancellationHandler)])
            .ShouldContain("INoCancellationHandler.HandleAsync must take a CancellationToken as its last parameter.");
    }
}

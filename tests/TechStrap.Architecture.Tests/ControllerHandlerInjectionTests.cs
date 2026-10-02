namespace TechStrap.Architecture.Tests;

public sealed class ControllerHandlerInjectionTests
{
    private static readonly Type[] ApiTypes = typeof(TechStrap.Api.Program).Assembly.GetTypes();

    [Fact]
    public void Api_controllers_take_handlers_through_FromServices_action_parameters_only()
    {
        // Passes vacuously until PHASE-04 adds the first controller; the rule is already enforced.
        HandlerRules.FindControllerInjectionViolations(ApiTypes).ShouldBeEmpty();
    }

    [Fact]
    public void A_controller_using_FromServices_passes()
    {
        HandlerRules.FindControllerInjectionViolations([typeof(HandlerFixtures.GoodController)]).ShouldBeEmpty();
    }

    [Fact]
    public void A_controller_with_a_constructor_injected_handler_is_flagged()
    {
        HandlerRules.FindControllerInjectionViolations([typeof(HandlerFixtures.ConstructorInjectedController)])
            .ShouldContain(violation => violation.Contains("constructor must not take IGoodSampleHandler", StringComparison.Ordinal));
    }

    [Fact]
    public void An_action_parameter_without_FromServices_is_flagged()
    {
        HandlerRules.FindControllerInjectionViolations([typeof(HandlerFixtures.MissingFromServicesController)])
            .ShouldContain(violation => violation.Contains("must use [FromServices]", StringComparison.Ordinal));
    }

    [Fact]
    public void An_action_depending_on_a_concrete_handler_is_flagged()
    {
        HandlerRules.FindControllerInjectionViolations([typeof(HandlerFixtures.ConcreteHandlerController)])
            .ShouldContain(violation => violation.Contains("must depend on the handler interface", StringComparison.Ordinal));
    }
}

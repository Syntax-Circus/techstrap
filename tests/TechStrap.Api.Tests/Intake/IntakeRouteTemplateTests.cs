using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using TechStrap.Api.Controllers;
using TechStrap.Contracts.Intake;

namespace TechStrap.Api.Tests.Intake;

public sealed class IntakeRouteTemplateTests
{
    [Fact]
    public void IntakeController_route_template_equals_IntakeRoutes_Tickets()
    {
        var route = typeof(IntakeController).GetCustomAttribute<RouteAttribute>();

        route.ShouldNotBeNull();
        route.Template.ShouldBe(IntakeRoutes.Tickets);
    }
}

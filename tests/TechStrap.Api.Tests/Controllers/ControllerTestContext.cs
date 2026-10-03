using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.AspNetCore.Common;

namespace TechStrap.Api.Tests.Controllers;

/// <summary>Creates a controller with just enough request services for ToActionResult's ProblemDetails mapping.</summary>
public static class ControllerTestContext
{
    public static TController For<TController>()
        where TController : ControllerBase, new()
    {
        var services = new ServiceCollection().AddLogging().AddResultProblemDetails().BuildServiceProvider();
        return new TController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } },
        };
    }
}

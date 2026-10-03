using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Api.Tests;

/// <summary>The OpenAPI document lists exactly the controller routes, so typed clients (PHASE-07, PHASE-11) can be generated from it.</summary>
public sealed partial class OpenApiSurfaceTests
{
    [GeneratedRegex(@":[^}]+")]
    private static partial Regex RouteConstraint();

    [Fact]
    public async Task The_openapi_document_lists_every_controller_route()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));

        var documented = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => $"{operation.Name.ToUpperInvariant()} {path.Name}"))
            .ToHashSet(StringComparer.Ordinal);
        var routed = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => $"{method} /{RouteConstraint().Replace(endpoint.RoutePattern.RawText!, string.Empty)}"))
            .ToHashSet(StringComparer.Ordinal);

        routed.Count.ShouldBeGreaterThanOrEqualTo(18);
        documented.ShouldBe(routed, ignoreOrder: true);
    }
}

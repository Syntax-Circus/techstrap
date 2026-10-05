using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests;

/// <summary>
/// The OpenAPI document says how a caller proves who they are (PHASE-07c; PHASE-11 generates the SDK from it): a bearer token for agents, an API key for intake, a ticket token for customers, and
/// nothing for a public operation. The document is documentation only, so these tests read the served document, not the policies.
/// </summary>
public sealed partial class OpenApiSecurityTests
{
    [GeneratedRegex(@":[^}]+")]
    private static partial Regex RouteConstraint();

    private static async Task<JsonDocument> GetDocumentAsync(ApiFactory factory)
    {
        using var client = factory.CreateClient();
        return JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));
    }

    private static string[] SecurityOf(JsonElement document, string path, string method)
    {
        var operation = document.GetProperty("paths").GetProperty(path).GetProperty(method);
        return operation.TryGetProperty("security", out var security)
            ? [.. security.EnumerateArray().SelectMany(requirement => requirement.EnumerateObject().Select(scheme => scheme.Name))]
            : [];
    }

    [Fact]
    public async Task The_document_declares_the_bearer_the_api_key_and_the_ticket_token_schemes()
    {
        await using var factory = new ApiFactory();
        using var document = await GetDocumentAsync(factory);

        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");

        schemes.EnumerateObject().Select(scheme => scheme.Name).Order().ShouldBe(["ApiKey", "Bearer", "TicketToken"]);
        var bearer = schemes.GetProperty("Bearer");
        bearer.GetProperty("type").GetString().ShouldBe("http");
        bearer.GetProperty("scheme").GetString().ShouldBe("bearer");
        bearer.GetProperty("bearerFormat").GetString().ShouldBe("JWT");
        var apiKey = schemes.GetProperty("ApiKey");
        apiKey.GetProperty("type").GetString().ShouldBe("apiKey");
        apiKey.GetProperty("in").GetString().ShouldBe("header");
        apiKey.GetProperty("name").GetString().ShouldBe("X-Api-Key");
        var ticket = schemes.GetProperty("TicketToken");
        ticket.GetProperty("type").GetString().ShouldBe("apiKey");
        ticket.GetProperty("in").GetString().ShouldBe("header");
        ticket.GetProperty("name").GetString().ShouldBe("X-Ticket-Token");
    }

    [Theory]
    [InlineData("/api/agents/me", "get", "Bearer")]
    [InlineData("/api/tickets", "get", "Bearer")]
    [InlineData("/api/tickets/{id}/replies", "post", "Bearer")]
    [InlineData("/api/products", "post", "Bearer")]
    [InlineData("/api/products/{id}/api-keys", "post", "Bearer")]
    [InlineData("/api/attachments/{id}", "get", "Bearer")]
    [InlineData("/api/kb/articles", "get", "Bearer")]
    [InlineData("/api/kb/articles/{id}/publish", "post", "Bearer")]
    [InlineData("/api/kb/preview", "post", "Bearer")]
    [InlineData("/api/kb/images", "post", "Bearer")]
    [InlineData("/api/kb/categories/{id}", "delete", "Bearer")]
    [InlineData("/api/intake/tickets", "post", "ApiKey")]
    [InlineData("/api/customer/ticket", "get", "TicketToken")]
    [InlineData("/api/customer/ticket/replies", "post", "TicketToken")]
    [InlineData("/api/customer/attachments/{id}", "get", "TicketToken")]
    public async Task An_operation_names_the_one_scheme_its_policy_needs(string path, string method, string scheme)
    {
        await using var factory = new ApiFactory();
        using var document = await GetDocumentAsync(factory);

        SecurityOf(document.RootElement, path, method).ShouldBe([scheme]);
    }

    [Theory]
    [InlineData("/api/customer/access-link", "post")]
    [InlineData("/api/public/products/{productKey}", "get")]
    [InlineData("/api/public/products/{productKey}/tickets", "post")]
    [InlineData("/api/public/kb/{productKey}/search", "get")]
    [InlineData("/api/public/kb/{productKey}/categories", "get")]
    [InlineData("/api/public/kb/{productKey}/articles/{categorySlug}/{slug}", "get")]
    [InlineData("/api/public/kb/{productKey}/sitemap", "get")]
    public async Task A_public_operation_names_no_scheme(string path, string method)
    {
        await using var factory = new ApiFactory();
        using var document = await GetDocumentAsync(factory);

        SecurityOf(document.RootElement, path, method).ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_operation_behind_an_agent_admin_or_api_key_policy_carries_a_requirement()
    {
        await using var factory = new ApiFactory();
        using var document = await GetDocumentAsync(factory);
        var guarded = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .Where(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data =>
                data.Policy is AuthorizationPolicies.Agent or AuthorizationPolicies.Admin or AuthorizationPolicies.ApiKey))
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (Path: "/" + RouteConstraint().Replace(endpoint.RoutePattern.RawText!, string.Empty), Method: method.ToLowerInvariant())))
            .Distinct()
            .ToList();

        guarded.Count.ShouldBeGreaterThanOrEqualTo(30);
        foreach (var (path, method) in guarded)
        {
            SecurityOf(document.RootElement, path, method).ShouldNotBeEmpty($"{method.ToUpperInvariant()} {path} is guarded but names no scheme");
        }
    }

    [Fact]
    public void Anonymous_beats_every_policy_and_a_ticket_token_header_alone_does_not_make_an_agent_route_a_customer_one()
    {
        OpenApiSecurity.SchemeFor([new AllowAnonymousAttribute(), new AuthorizeAttribute(AuthorizationPolicies.Agent)], takesTicketToken: true).ShouldBeNull();
        OpenApiSecurity.SchemeFor([new AuthorizeAttribute(AuthorizationPolicies.Admin)], takesTicketToken: true).ShouldBe(OpenApiSecurity.Bearer);
        OpenApiSecurity.SchemeFor([new AuthorizeAttribute(AuthorizationPolicies.Agent), new AuthorizeAttribute(AuthorizationPolicies.Admin)], takesTicketToken: false).ShouldBe(OpenApiSecurity.Bearer);
        OpenApiSecurity.SchemeFor([new AuthorizeAttribute(AuthorizationPolicies.ApiKey)], takesTicketToken: false).ShouldBe(OpenApiSecurity.ApiKey);
        OpenApiSecurity.SchemeFor([new AuthorizeAttribute(AuthorizationPolicies.Public)], takesTicketToken: true).ShouldBe(OpenApiSecurity.TicketToken);
        OpenApiSecurity.SchemeFor([new AuthorizeAttribute(AuthorizationPolicies.Public)], takesTicketToken: false).ShouldBeNull();
        OpenApiSecurity.SchemeFor([], takesTicketToken: false).ShouldBeNull();
    }
}

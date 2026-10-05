using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using TechStrap.Api.Security;
using TechStrap.Contracts.Http;

namespace TechStrap.Api.Startup;

/// <summary>
/// Documents how a caller proves who they are (PHASE-07c, PHASE-11): the typed clients and the SDK are generated from the OpenAPI document, so it must say which operations take which credential.
/// Three schemes, one per way in (02-ARCHITECTURE section 4): <see cref="Bearer"/> for an agent's OIDC access token (the Agent and Admin policies), <see cref="ApiKey"/> for a product's key on intake,
/// and <see cref="TicketToken"/> for a customer's access token. An operation names the scheme its policy needs; a public operation names none. The document is the same for everyone and is
/// documentation only: the policies on the controllers still decide, and nothing here changes what the API accepts.
/// </summary>
public static class OpenApiSecurity
{
    public const string Bearer = "Bearer";
    public const string ApiKey = "ApiKey";
    public const string TicketToken = "TicketToken";

    /// <summary>Adds the three security schemes to the document and the matching security requirement to every operation that is not public.</summary>
    public static OpenApiOptions AddTechStrapSecuritySchemes(this OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[Bearer] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "An agent's access token from the identity provider (OIDC). Sent as Authorization: Bearer <token>.",
            };
            document.Components.SecuritySchemes[ApiKey] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = HeaderNames.ApiKey,
                Description = "A product API key, for ticket intake from the product's own backend.",
            };
            document.Components.SecuritySchemes[TicketToken] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = HeaderNames.TicketToken,
                Description = "A customer's ticket access token, from the link in the email they were sent.",
            };
            return Task.CompletedTask;
        });
        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            var scheme = SchemeFor(metadata, TakesTicketToken(operation));
            if (scheme is not null)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(scheme, context.Document)] = [],
                });
            }

            return Task.CompletedTask;
        });
        return options;
    }

    /// <summary>
    /// The scheme an operation needs, from its authorization metadata, or null for a public one. Agent and Admin routes take the bearer token, the ApiKey policy the product key. A route that is
    /// public by policy but reads the customer's <c>X-Ticket-Token</c> header takes the ticket token. <c>[AllowAnonymous]</c> wins over everything.
    /// </summary>
    internal static string? SchemeFor(IEnumerable<object> endpointMetadata, bool takesTicketToken)
    {
        var metadata = endpointMetadata.ToList();
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return null;
        }

        var policies = metadata.OfType<IAuthorizeData>().Select(data => data.Policy).ToList();
        if (policies.Contains(AuthorizationPolicies.Admin) || policies.Contains(AuthorizationPolicies.Agent))
        {
            return Bearer;
        }

        if (policies.Contains(AuthorizationPolicies.ApiKey))
        {
            return ApiKey;
        }

        return takesTicketToken ? TicketToken : null;
    }

    private static bool TakesTicketToken(Microsoft.OpenApi.OpenApiOperation operation) =>
        operation.Parameters?.Any(parameter => parameter.In == ParameterLocation.Header
            && string.Equals(parameter.Name, HeaderNames.TicketToken, StringComparison.OrdinalIgnoreCase)) == true;
}

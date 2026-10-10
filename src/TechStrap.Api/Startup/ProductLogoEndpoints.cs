using TechStrap.Application.Products;

namespace TechStrap.Api.Startup;

/// <summary>
/// <c>GET /product-logos/{name}</c>, anonymous (D-021, D-052). Like <c>/kb-images/{name}</c> it runs no application workflow, reads only an object whose name is exactly the shape the store writes, and serves it with headers that stop a
/// browser treating it as anything but an image. The route is outside <c>api/</c> and is not part of the API contract, so the route-policy coverage test and the OpenAPI document leave it out; <c>ProductLogoServingTests</c> pins its behaviour.
/// </summary>
public static class ProductLogoEndpoints
{
    public const string Route = "/product-logos/{name}";

    public static IEndpointRouteBuilder MapProductLogos(this IEndpointRouteBuilder app)
    {
        app.MapMethods(Route, ["GET", "HEAD"], async (string name, IProductLogoStore store, HttpContext context, CancellationToken cancellationToken) =>
        {
            // A trailing slash is not an address the store wrote: 404 rather than serving the same file at a second URL.
            var logo = context.Request.Path.Value!.EndsWith('/') ? null : await store.OpenReadAsync(name, cancellationToken);
            if (logo is null)
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.NotFound();
            }

            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.CacheControl = "public, max-age=31536000, immutable";
            headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            if (logo.Size >= 0)
            {
                context.Response.ContentLength = logo.Size;
            }

            // Results.Stream disposes the stream when the response is done.
            return Results.Stream(logo.Content, logo.ContentType);
        }).AllowAnonymous().ExcludeFromDescription();
        return app;
    }
}

using TechStrap.Application.Knowledge;

namespace TechStrap.Api.Startup;

/// <summary>
/// <c>GET /kb-images/{name}</c>, anonymous (D-021, D-044). It is the one public file route: it runs no application workflow, reads only an object
/// whose name is exactly the shape the store writes, and serves it with headers that stop a browser treating it as anything but an image.
/// The route is outside <c>api/</c> and is not part of the API contract, so the route-policy coverage test and the OpenAPI document leave it out; <c>KbImageServingTests</c> pins its behaviour.
/// </summary>
public static class KbImageEndpoints
{
    public const string Route = "/kb-images/{name}";

    public static IEndpointRouteBuilder MapKbImages(this IEndpointRouteBuilder app)
    {
        app.MapMethods(Route, ["GET", "HEAD"], async (string name, IKbImageStore store, HttpContext context, CancellationToken cancellationToken) =>
        {
            // A trailing slash is not an address the store wrote: 404 rather than serving the same file at a second URL.
            var image = context.Request.Path.Value!.EndsWith('/') ? null : await store.OpenReadAsync(name, cancellationToken);
            if (image is null)
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.NotFound();
            }

            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.CacheControl = "public, max-age=31536000, immutable";
            headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            if (image.Size >= 0)
            {
                context.Response.ContentLength = image.Size;
            }

            // Results.Stream disposes the stream when the response is done.
            return Results.Stream(image.Content, image.ContentType);
        }).AllowAnonymous().ExcludeFromDescription();
        return app;
    }
}

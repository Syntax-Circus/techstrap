using System.Net;
using System.Text;
using TechStrap.WebhookCatcher;

// Local development only: the Worker's webhook deliveries land here (as its mail lands in Mailpit) so they can be read in a browser.
var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("The webhook catcher runs only in Development: it has no sign-in and shows every captured delivery.");
}

// Binding to loopback does not stop DNS rebinding (a web page whose own name resolves to 127.0.0.1), so only local Host
// headers are accepted. The Worker posts to 127.0.0.1; Aspire's proxy passes the header on.
builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;[::1]";
builder.Services.AddSingleton<CaptureStore>();
var app = builder.Build();

// A second guard if it is ever bound to more than localhost. A null address means there is no network connection (an in-memory test server).
app.Use((context, next) =>
{
    if (context.Connection.RemoteIpAddress is { } address && !IPAddress.IsLoopback(address))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    return next(context);
});

app.MapPost("/capture", async (HttpRequest request, CaptureStore store, CancellationToken cancellationToken) =>
{
    // One byte more than the limit tells a body that is too long from one that just fits, with or without a Content-Length.
    var buffer = new byte[CaptureStore.MaxBodyBytes + 1];
    var length = await request.Body.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
    if (length > CaptureStore.MaxBodyBytes)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

    string? eventId = request.Headers["X-TechStrap-Event-Id"];
    store.Add(new CapturedRequest(
        DateTimeOffset.UtcNow,
        request.Headers["X-TechStrap-Capture-Original-Host"].ToString(),
        eventId,
        request.ContentType ?? string.Empty,
        Encoding.UTF8.GetString(buffer, 0, length)));
    return Results.NoContent();
});
app.MapGet("/", (CaptureStore store) => Results.Content(CapturePage.Render(store.Snapshot()), "text/html; charset=utf-8"));
app.MapGet("/api/requests", (CaptureStore store) => store.Snapshot());
app.MapPost("/clear", (CaptureStore store) =>
{
    store.Clear();
    return Results.Redirect("/");
});
app.MapGet("/health", () => "Healthy");

app.Run();

namespace TechStrap.WebhookCatcher
{
    public partial class Program;
}

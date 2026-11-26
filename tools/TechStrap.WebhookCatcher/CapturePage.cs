using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

namespace TechStrap.WebhookCatcher;

/// <summary>
/// The list page. Everything in a captured delivery is untrusted (a ticket title travels in the body), so every captured value goes through
/// <see cref="HtmlEncoder"/> and is only ever element text, never an attribute, URL or script.
/// </summary>
internal static class CapturePage
{
    public static string Render(IReadOnlyList<CapturedRequest> requests)
    {
        var html = new StringBuilder(
            """
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>TechStrap webhook catcher</title>
            <style>
            body { font-family: system-ui, sans-serif; margin: 2rem; }
            article { border-top: 1px solid #ccc; padding: 0.5rem 0; }
            pre { background: #f4f4f4; padding: 0.75rem; white-space: pre-wrap; overflow-wrap: anywhere; }
            </style>
            </head>
            <body>
            <h1>Webhook catcher</h1>
            <form method="post" action="/clear"><button type="submit">Clear</button></form>

            """);
        html.Append(CultureInfo.InvariantCulture, $"<p>{requests.Count} captured, newest first. The newest {CaptureStore.Capacity} are kept. Reload to refresh.</p>\n");

        foreach (var request in requests)
        {
            html.Append("<article>\n<dl>\n");
            AppendField(html, "Received (UTC)", request.ReceivedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            AppendField(html, "Original host", request.OriginalHost);
            AppendField(html, "Event id", request.EventId ?? "(none)");
            AppendField(html, "Content type", request.ContentType);
            html.Append("</dl>\n<pre>").Append(Encode(request.Body)).Append("</pre>\n</article>\n");
        }

        return html.Append("</body>\n</html>\n").ToString();
    }

    private static void AppendField(StringBuilder html, string name, string value) =>
        html.Append("<dt>").Append(name).Append("</dt><dd>").Append(Encode(value)).Append("</dd>\n");

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
}

using AngleSharp.Dom;
using GanssHtmlSanitizer = Ganss.Xss.HtmlSanitizer;

namespace TechStrap.Infrastructure.Content;

/// <summary>
/// The KB allow-list sanitiser (D-044): the message profile plus tables and <c>img</c>. It is the second line of defence behind Markdig's
/// disabled raw HTML, and it is tested on its own. An image keeps only an absolute http or https <c>src</c> and its <c>alt</c>; an image
/// with any other source (relative, protocol-relative, <c>data:</c>, <c>javascript:</c>, <c>mailto:</c>) is removed.
/// </summary>
internal sealed class KbHtmlSanitizer
{
    private static readonly string[] _tags =
    [
        "p", "br", "strong", "b", "em", "i", "u", "a", "ul", "ol", "li", "blockquote", "code", "pre", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "del", "s",
        "img", "table", "thead", "tbody", "tr", "th", "td",
    ];

    private readonly GanssHtmlSanitizer _sanitizer;

    public KbHtmlSanitizer()
    {
        _sanitizer = new GanssHtmlSanitizer();
        _sanitizer.AllowedTags.Clear();
        _sanitizer.AllowedAttributes.Clear();
        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.AllowedClasses.Clear();
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.UriAttributes.Clear();
        _sanitizer.UriAttributes.Add("href");
        _sanitizer.UriAttributes.Add("src");
        foreach (var tag in _tags)
        {
            _sanitizer.AllowedTags.Add(tag);
        }

        _sanitizer.AllowedAttributes.Add("href");
        _sanitizer.AllowedAttributes.Add("src");
        _sanitizer.AllowedAttributes.Add("alt");
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not IElement element)
            {
                return;
            }

            if (string.Equals(element.LocalName, "a", StringComparison.OrdinalIgnoreCase))
            {
                element.SetAttribute("rel", "noopener noreferrer nofollow");
            }
            else if (string.Equals(element.LocalName, "img", StringComparison.OrdinalIgnoreCase))
            {
                if (IsAbsoluteWebUrl(element.GetAttribute("src")))
                {
                    element.SetAttribute("loading", "lazy");
                    element.SetAttribute("referrerpolicy", "no-referrer");
                }
                else
                {
                    element.Remove();
                }
            }
        };
    }

    public string Sanitize(string html) => _sanitizer.Sanitize(html);

    // The sanitiser has already dropped any scheme that is not allowed; a mailto: or a relative path is still a valid link target, but never an image source.
    private static bool IsAbsoluteWebUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

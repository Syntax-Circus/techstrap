using GanssHtmlSanitizer = Ganss.Xss.HtmlSanitizer;
using TechStrap.Application.Content;
using AngleSharp.Dom;

namespace TechStrap.Infrastructure.Content;

/// <summary>Allow-list HTML sanitizer: basic formatting and http/https/mailto links only (D-014).</summary>
internal sealed class HtmlSanitizerAdapter : IHtmlSanitizer
{
    private static readonly string[] _tags =
        ["p", "br", "strong", "b", "em", "i", "u", "a", "ul", "ol", "li", "blockquote", "code", "pre", "h1", "h2", "h3", "h4"];

    private readonly GanssHtmlSanitizer _sanitizer;

    public HtmlSanitizerAdapter()
    {
        _sanitizer = new GanssHtmlSanitizer();
        _sanitizer.AllowedTags.Clear();
        _sanitizer.AllowedAttributes.Clear();
        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.AllowedClasses.Clear();
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.UriAttributes.Clear();
        _sanitizer.UriAttributes.Add("href");
        foreach (var tag in _tags)
        {
            _sanitizer.AllowedTags.Add(tag);
        }

        _sanitizer.AllowedAttributes.Add("href");
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is IElement element && string.Equals(element.LocalName, "a", StringComparison.OrdinalIgnoreCase))
            {
                element.SetAttribute("rel", "noopener noreferrer nofollow");
            }
        };
    }

    public string Sanitize(string html) => _sanitizer.Sanitize(html);
}

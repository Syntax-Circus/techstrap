namespace TechStrap.Application.Content;

/// <summary>Removes unsafe markup from HTML before it is stored or shown (D-014). Reused by the knowledge base (PHASE-08).</summary>
public interface IHtmlSanitizer
{
    string Sanitize(string html);
}

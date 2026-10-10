using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TechStrap.Application.Email;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Products;

namespace TechStrap.Infrastructure.Email;

/// <summary>Renders customer emails with the product's branding leading; TechStrap appears only as the optional Powered-by line (BRAND.md).</summary>
internal sealed class EmailTemplateRenderer(IOptions<EmailBrandingOptions> options) : IEmailTemplateRenderer
{
    private const string PoweredByText = "Powered by TechStrap";

    private const string ParagraphStyle = "margin:0 0 16px 0;";

    public RenderedEmail RenderTicketConfirmation(TicketConfirmationEmail model, EmailBranding branding)
    {
        var showPoweredBy = options.Value.ShowPoweredBy;
        var subject = $"[{model.TicketNumber}] We've got your request: {model.Subject}";
        return Build(subject, BuildText(model, branding, showPoweredBy), BuildHtml(model, branding, showPoweredBy), branding);
    }

    private static string BuildText(TicketConfirmationEmail model, EmailBranding branding, bool showPoweredBy)
    {
        var text = new StringBuilder();
        text.Append(string.IsNullOrWhiteSpace(model.RequesterName) ? "Hi," : $"Hi {model.RequesterName},").Append("\n\n");
        text.Append($"We've got your request and created ticket {model.TicketNumber}.").Append("\n\n");
        text.Append($"Follow it here: {model.PortalLink}").Append("\n\n");
        if (!string.IsNullOrWhiteSpace(model.AgentPublicName))
        {
            text.Append($"{model.AgentPublicName} is looking after it.").Append("\n\n");
        }

        text.Append($"The {branding.DisplayName} team");
        if (showPoweredBy)
        {
            text.Append("\n\n").Append($"{PoweredByText}: {EmailBrandingOptions.PoweredByUrl}");
        }

        return text.ToString();
    }

    private static string BuildHtml(TicketConfirmationEmail model, EmailBranding branding, bool showPoweredBy)
    {
        var colors = Colors(branding);

        var name = Encode(branding.DisplayName);
        var link = Encode(model.PortalLink);
        var greeting = string.IsNullOrWhiteSpace(model.RequesterName) ? "Hi," : $"Hi {Encode(model.RequesterName)},";

        var html = new StringBuilder();
        html.Append($"<p style=\"margin:0 0 16px 0;\">{greeting}</p>");
        html.Append($"<p style=\"margin:0 0 16px 0;\">We've got your request and created ticket <strong>{Encode(model.TicketNumber)}</strong>: {Encode(model.Subject)}</p>");
        if (!string.IsNullOrWhiteSpace(model.AgentPublicName))
        {
            html.Append($"<p style=\"margin:0 0 16px 0;\">{Encode(model.AgentPublicName)} is looking after it.</p>");
        }

        html.Append($"<p style=\"margin:0 0 16px 0;\"><a href=\"{link}\" style=\"display:inline-block;background:{colors.Accent};color:{colors.OnAccent};padding:12px 20px;text-decoration:none;font-weight:bold;border-radius:4px;\">Follow your request</a></p>");
        html.Append($"<p style=\"margin:0 0 16px 0;font-size:14px;\">Or open this link: <a href=\"{link}\" style=\"color:{colors.AccentInk};\">{link}</a></p>");
        html.Append($"<p style=\"margin:0;\">The {name} team</p>");
        return Layout(branding, colors, html.ToString(), showPoweredBy);
    }

    public RenderedEmail RenderAgentReply(AgentReplyEmail model, string messageHtml, EmailBranding branding)
    {
        var showPoweredBy = options.Value.ShowPoweredBy;
        var subject = $"[{model.TicketNumber}] Re: {model.Subject}";
        var link = Encode(model.PortalLink);
        var solvedLine = $"We've marked this request as solved. Reply within {Days(model.ReopenDays > 0 ? model.ReopenDays : TicketNotices.DefaultReopenDays)} if you need anything else.";

        var text = new StringBuilder();
        text.Append(Greeting(model.RequesterName)).Append("\n\n");
        text.Append($"{model.AgentPublicName} replied:").Append("\n\n");
        text.Append(HtmlText.ToPlainText(messageHtml)).Append("\n\n");
        var articles = WebArticleLinks(model.Articles);
        if (articles.Count > 0)
        {
            text.Append("Related articles:").Append('\n');
            foreach (var article in articles)
            {
                text.Append($"- {OneLine(article.Title)}: {article.Url}").Append('\n');
            }

            text.Append('\n');
        }

        if (model.Solved)
        {
            text.Append(solvedLine).Append("\n\n");
        }

        text.Append($"View your request: {model.PortalLink}").Append("\n\n");
        text.Append($"The {branding.DisplayName} team");
        AppendTextPoweredBy(text, showPoweredBy);

        var colors = Colors(branding);
        var body = new StringBuilder();
        body.Append($"<p style=\"{ParagraphStyle}\">{HtmlGreeting(model.RequesterName)}</p>");
        body.Append($"<p style=\"{ParagraphStyle}\">{Encode(model.AgentPublicName)} replied:</p>");
        body.Append($"<div style=\"margin:0 0 16px 0;\">{messageHtml}</div>");
        if (articles.Count > 0)
        {
            body.Append("<p style=\"margin:0 0 8px 0;\">Related articles:</p><ul style=\"margin:0 0 16px 0;padding-left:20px;\">");
            foreach (var article in articles)
            {
                body.Append($"<li><a href=\"{Encode(article.Url)}\" style=\"color:{colors.AccentInk};\">{Encode(article.Title)}</a></li>");
            }

            body.Append("</ul>");
        }

        if (model.Solved)
        {
            body.Append($"<p style=\"{ParagraphStyle}\">{Encode(solvedLine)}</p>");
        }

        body.Append(Button(link, "View your request", colors));
        body.Append(FallbackLink(link, colors));
        body.Append($"<p style=\"margin:0;\">The {Encode(branding.DisplayName)} team</p>");
        return Build(subject, text.ToString(), Layout(branding, colors, body.ToString(), showPoweredBy), branding);
    }

    private static string OneLine(string value) => System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ").Trim();

    // At most the reply limit, and only absolute http or https addresses: a link that is not one is dropped rather than emailed broken (D-044).
    private static List<ArticleLinkEntry> WebArticleLinks(IReadOnlyList<ArticleLinkEntry>? articles) =>
        [.. (articles ?? []).Where(article => article is not null && IsWebUrl(article.Url) && !string.IsNullOrWhiteSpace(article.Title)).Take(TicketOperationLimits.MaxLinkedArticles)];

    public RenderedEmail RenderTicketSolved(TicketSolvedEmail model, EmailBranding branding)
    {
        var showPoweredBy = options.Value.ShowPoweredBy;
        var subject = $"[{model.TicketNumber}] Solved: {model.Subject}";
        var link = Encode(model.PortalLink);
        var line = $"We've marked your request as solved. If you need anything else, reply within {Days(model.ReopenDays)}, or use the link below.";

        var text = new StringBuilder();
        text.Append(Greeting(model.RequesterName)).Append("\n\n");
        text.Append(line).Append("\n\n");
        text.Append($"View your request: {model.PortalLink}").Append("\n\n");
        text.Append($"The {branding.DisplayName} team");
        AppendTextPoweredBy(text, showPoweredBy);

        var colors = Colors(branding);
        var body = new StringBuilder();
        body.Append($"<p style=\"{ParagraphStyle}\">{HtmlGreeting(model.RequesterName)}</p>");
        body.Append($"<p style=\"{ParagraphStyle}\">{Encode(line)}</p>");
        body.Append(Button(link, "View your request", colors));
        body.Append(FallbackLink(link, colors));
        body.Append($"<p style=\"margin:0;\">The {Encode(branding.DisplayName)} team</p>");
        return Build(subject, text.ToString(), Layout(branding, colors, body.ToString(), showPoweredBy), branding);
    }

    // An internal alert to an agent: the Powered-by line is never shown.
    public RenderedEmail RenderTicketAssigned(TicketAssignedEmail model, EmailBranding branding)
    {
        var subject = $"[{model.TicketNumber}] Assigned to you: {model.Subject}";
        var hasAssigner = !string.IsNullOrWhiteSpace(model.AssignedByName);
        var adminLink = model.AdminLink is { } candidate && IsWebUrl(candidate) ? candidate : null;

        var text = new StringBuilder();
        text.Append($"Ticket {model.TicketNumber} ({model.Subject}) has been assigned to you.").Append("\n\n");
        text.Append($"Product: {model.ProductName}").Append('\n');
        if (hasAssigner)
        {
            text.Append($"Assigned by {model.AssignedByName}").Append('\n');
        }

        text.Append('\n').Append(adminLink is null ? "Open TechStrap to work on it." : $"Open in TechStrap: {adminLink}");

        var colors = Colors(branding);
        var body = new StringBuilder();
        body.Append($"<p style=\"{ParagraphStyle}\">Ticket <strong>{Encode(model.TicketNumber)}</strong> ({Encode(model.Subject)}) has been assigned to you.</p>");
        body.Append($"<p style=\"{ParagraphStyle}\">Product: {Encode(model.ProductName)}");
        if (hasAssigner)
        {
            body.Append($"<br>Assigned by {Encode(model.AssignedByName!)}");
        }

        body.Append("</p>");
        body.Append(adminLink is null
            ? "<p style=\"margin:0;\">Open TechStrap to work on it.</p>"
            : Button(Encode(adminLink), "Open in TechStrap", colors));
        return Build(subject, text.ToString(), Layout(branding, colors, body.ToString(), showPoweredBy: false), branding);
    }

    // Internal alerts to an agent: the Powered-by line is never shown, and the Admin button only for http(s) links.
    public RenderedEmail RenderNewTicketAlert(NewTicketAlertEmail model, EmailBranding branding)
    {
        var kind = model.IsFollowUp ? "New follow-up" : "New ticket";
        var subject = $"[{model.TicketNumber}] {kind}: {model.Subject}";
        var intro = model.IsFollowUp
            ? $"A follow-up ticket {model.TicketNumber} ({model.Subject}) has been opened."
            : $"A new ticket {model.TicketNumber} ({model.Subject}) has been opened.";
        var adminLink = WebLinkOrNull(model.AdminLink);

        var text = new StringBuilder();
        text.Append(intro).Append("\n\n");
        text.Append($"Product: {model.ProductName}").Append('\n');
        text.Append($"From: {model.RequesterLabel}").Append("\n\n");
        text.Append(adminLink is null ? "Open TechStrap to work on it." : $"Open in TechStrap: {adminLink}");

        var colors = Colors(branding);
        var body = new StringBuilder();
        body.Append($"<p style=\"{ParagraphStyle}\">{(model.IsFollowUp ? "A follow-up ticket" : "A new ticket")} <strong>{Encode(model.TicketNumber)}</strong> ({Encode(model.Subject)}) has been opened.</p>");
        body.Append($"<p style=\"{ParagraphStyle}\">Product: {Encode(model.ProductName)}<br>From: {Encode(model.RequesterLabel)}</p>");
        body.Append(AdminAction(adminLink, colors));
        return Build(subject, text.ToString(), Layout(branding, colors, body.ToString(), showPoweredBy: false), branding);
    }

    public RenderedEmail RenderCustomerReplyAlert(CustomerReplyAlertEmail model, EmailBranding branding)
    {
        var subject = $"[{model.TicketNumber}] Customer replied: {model.Subject}";
        var intro = $"The customer replied on ticket {model.TicketNumber} ({model.Subject}).";
        const string reopenedLine = "The ticket was reopened.";
        var adminLink = WebLinkOrNull(model.AdminLink);

        var text = new StringBuilder();
        text.Append(intro).Append("\n\n");
        text.Append($"Product: {model.ProductName}").Append('\n');
        if (model.Reopened)
        {
            text.Append(reopenedLine).Append('\n');
        }

        text.Append('\n').Append(adminLink is null ? "Open TechStrap to work on it." : $"Open in TechStrap: {adminLink}");

        var colors = Colors(branding);
        var body = new StringBuilder();
        body.Append($"<p style=\"{ParagraphStyle}\">The customer replied on ticket <strong>{Encode(model.TicketNumber)}</strong> ({Encode(model.Subject)}).</p>");
        body.Append($"<p style=\"{ParagraphStyle}\">Product: {Encode(model.ProductName)}");
        if (model.Reopened)
        {
            body.Append($"<br>{reopenedLine}");
        }

        body.Append("</p>");
        body.Append(AdminAction(adminLink, colors));
        return Build(subject, text.ToString(), Layout(branding, colors, body.ToString(), showPoweredBy: false), branding);
    }

    // Customer-facing lost-link email: product branding leads, Powered-by follows the option.
    public RenderedEmail RenderAccessLinks(AccessLinksEmail model, EmailBranding branding)
    {
        var showPoweredBy = options.Value.ShowPoweredBy;
        const string subject = "Your request links";
        const string intro = "Here are the links to your requests.";

        var text = new StringBuilder();
        text.Append(Greeting(model.RequesterName)).Append("\n\n");
        text.Append(intro).Append("\n\n");
        foreach (var entry in model.Links)
        {
            text.Append($"{entry.TicketNumber}: {entry.Subject}").Append('\n');
            text.Append(entry.PortalLink).Append("\n\n");
        }

        text.Append($"The {branding.DisplayName} team");
        AppendTextPoweredBy(text, showPoweredBy);

        var colors = Colors(branding);
        var body = new StringBuilder();
        body.Append($"<p style=\"{ParagraphStyle}\">{HtmlGreeting(model.RequesterName)}</p>");
        body.Append($"<p style=\"{ParagraphStyle}\">{intro}</p>");
        foreach (var entry in model.Links)
        {
            var link = Encode(entry.PortalLink);
            body.Append($"<p style=\"{ParagraphStyle}\"><strong>{Encode(entry.TicketNumber)}</strong>: {Encode(entry.Subject)}</p>");
            body.Append(Button(link, "View your request", colors));
            body.Append(FallbackLink(link, colors));
        }

        body.Append($"<p style=\"margin:0;\">The {Encode(branding.DisplayName)} team</p>");
        return Build(subject, text.ToString(), Layout(branding, colors, body.ToString(), showPoweredBy), branding);
    }

    private static string Days(int count) => count == 1 ? "1 day" : $"{count} days";

    private static string? WebLinkOrNull(string? link) => link is { } candidate && IsWebUrl(candidate) ? candidate : null;

    private static string AdminAction(string? adminLink, ProductAccentColors colors) =>
        adminLink is null
            ? "<p style=\"margin:0;\">Open TechStrap to work on it.</p>"
            : Button(Encode(adminLink), "Open in TechStrap", colors);

    private static bool IsWebUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";

    private static RenderedEmail Build(string subject, string text, string html, EmailBranding branding)
    {
        var from = string.IsNullOrWhiteSpace(branding.FromAddress) ? null : $"{branding.DisplayName} <{branding.FromAddress}>";
        return new RenderedEmail(subject, text, html, from, branding.ReplyTo);
    }

    private static string Greeting(string? requesterName) => string.IsNullOrWhiteSpace(requesterName) ? "Hi," : $"Hi {requesterName},";

    private static string HtmlGreeting(string? requesterName) => string.IsNullOrWhiteSpace(requesterName) ? "Hi," : $"Hi {Encode(requesterName)},";

    private static void AppendTextPoweredBy(StringBuilder text, bool showPoweredBy)
    {
        if (showPoweredBy)
        {
            text.Append("\n\n").Append($"{PoweredByText}: {EmailBrandingOptions.PoweredByUrl}");
        }
    }

    private static string Button(string encodedLink, string label, ProductAccentColors colors) =>
        $"<p style=\"{ParagraphStyle}\"><a href=\"{encodedLink}\" style=\"display:inline-block;background:{colors.Accent};color:{colors.OnAccent};padding:12px 20px;text-decoration:none;font-weight:bold;border-radius:4px;\">{label}</a></p>";

    private static string FallbackLink(string encodedLink, ProductAccentColors colors) =>
        $"<p style=\"{ParagraphStyle}font-size:14px;\">Or open this link: <a href=\"{encodedLink}\" style=\"color:{colors.AccentInk};\">{encodedLink}</a></p>";

    private static string Layout(EmailBranding branding, ProductAccentColors colors, string body, bool showPoweredBy)
    {
        var name = Encode(branding.DisplayName);
        var logo = branding.LogoPath is { } path && path.StartsWith("https://", StringComparison.Ordinal)
            ? $"<img src=\"{Encode(path)}\" alt=\"{name}\" height=\"32\" style=\"display:block;border:0;height:32px;margin:0 0 8px 0;\">"
            : string.Empty;

        // Only the header bar takes the chrome; the value written is TryDerive's normalized output, never the raw string.
        var (barFill, barText) = branding.ChromeColour is { } chromeColour && ProductAccent.TryDerive(chromeColour, out var chrome)
            ? (chrome.Accent, chrome.OnAccent)
            : (colors.Accent, colors.OnAccent);

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"></head>");
        html.Append("<body style=\"margin:0;padding:0;background:#F4F4F5;\">");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#F4F4F5;\"><tr><td align=\"center\" style=\"padding:24px 12px;\">");
        html.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;max-width:600px;background:#FFFFFF;font-family:Arial,Helvetica,sans-serif;color:#18181B;\">");
        html.Append($"<tr><td style=\"background:{barFill};color:{barText};padding:20px 24px;font-size:20px;font-weight:bold;\">{logo}{name}</td></tr>");
        html.Append("<tr><td style=\"padding:24px;font-size:16px;line-height:24px;\">");
        html.Append(body);
        html.Append("</td></tr>");
        if (showPoweredBy)
        {
            html.Append($"<tr><td style=\"padding:16px 24px;font-size:12px;color:#52525B;border-top:1px solid #E4E4E7;\"><a href=\"{EmailBrandingOptions.PoweredByUrl}\" style=\"color:#52525B;\">{PoweredByText}</a></td></tr>");
        }

        html.Append("</table></td></tr></table></body></html>");
        return html.ToString();
    }

    private static ProductAccentColors Colors(EmailBranding branding) =>
        ProductAccent.TryDerive(branding.AccentColour, out var derived)
            ? derived
            : ProductAccent.TryDerive(ProductBranding.DefaultAccentColour, out var fallback)
                ? fallback
                : new ProductAccentColors(ProductBranding.DefaultAccentColour, "#FFFFFF", ProductBranding.DefaultAccentColour);

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}

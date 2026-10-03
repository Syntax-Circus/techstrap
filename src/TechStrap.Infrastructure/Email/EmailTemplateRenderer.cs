using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TechStrap.Application.Email;
using TechStrap.Contracts.Branding;
using TechStrap.Domain.Products;

namespace TechStrap.Infrastructure.Email;

/// <summary>Renders customer emails with the product's branding leading; TechStrap appears only as the optional Powered-by line (BRAND.md).</summary>
internal sealed class EmailTemplateRenderer(IOptions<EmailBrandingOptions> options) : IEmailTemplateRenderer
{
    private const string PoweredByText = "Powered by TechStrap";

    public RenderedEmail RenderTicketConfirmation(TicketConfirmationEmail model, EmailBranding branding)
    {
        var showPoweredBy = options.Value.ShowPoweredBy;
        var subject = $"[{model.TicketNumber}] We've got your request: {model.Subject}";
        var from = string.IsNullOrWhiteSpace(branding.FromAddress) ? null : $"{branding.DisplayName} <{branding.FromAddress}>";
        return new RenderedEmail(subject, BuildText(model, branding, showPoweredBy), BuildHtml(model, branding, showPoweredBy), from, branding.ReplyTo);
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
        var colors = ProductAccent.TryDerive(branding.AccentColour, out var derived)
            ? derived
            : ProductAccent.TryDerive(ProductBranding.DefaultAccentColour, out var fallback)
                ? fallback
                : new ProductAccentColors(ProductBranding.DefaultAccentColour, "#FFFFFF", ProductBranding.DefaultAccentColour);

        var name = Encode(branding.DisplayName);
        var link = Encode(model.PortalLink);
        var greeting = string.IsNullOrWhiteSpace(model.RequesterName) ? "Hi," : $"Hi {Encode(model.RequesterName)},";
        var logo = branding.LogoPath is { } path && path.StartsWith("https://", StringComparison.Ordinal)
            ? $"<img src=\"{Encode(path)}\" alt=\"{name}\" height=\"32\" style=\"display:block;border:0;height:32px;margin:0 0 8px 0;\">"
            : string.Empty;

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"></head>");
        html.Append("<body style=\"margin:0;padding:0;background:#F4F4F5;\">");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#F4F4F5;\"><tr><td align=\"center\" style=\"padding:24px 12px;\">");
        html.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;max-width:600px;background:#FFFFFF;font-family:Arial,Helvetica,sans-serif;color:#18181B;\">");
        html.Append($"<tr><td style=\"background:{colors.Accent};color:{colors.OnAccent};padding:20px 24px;font-size:20px;font-weight:bold;\">{logo}{name}</td></tr>");
        html.Append("<tr><td style=\"padding:24px;font-size:16px;line-height:24px;\">");
        html.Append($"<p style=\"margin:0 0 16px 0;\">{greeting}</p>");
        html.Append($"<p style=\"margin:0 0 16px 0;\">We've got your request and created ticket <strong>{Encode(model.TicketNumber)}</strong>: {Encode(model.Subject)}</p>");
        if (!string.IsNullOrWhiteSpace(model.AgentPublicName))
        {
            html.Append($"<p style=\"margin:0 0 16px 0;\">{Encode(model.AgentPublicName)} is looking after it.</p>");
        }

        html.Append($"<p style=\"margin:0 0 16px 0;\"><a href=\"{link}\" style=\"display:inline-block;background:{colors.Accent};color:{colors.OnAccent};padding:12px 20px;text-decoration:none;font-weight:bold;border-radius:4px;\">Follow your request</a></p>");
        html.Append($"<p style=\"margin:0 0 16px 0;font-size:14px;\">Or open this link: <a href=\"{link}\" style=\"color:{colors.AccentInk};\">{link}</a></p>");
        html.Append($"<p style=\"margin:0;\">The {name} team</p>");
        html.Append("</td></tr>");
        if (showPoweredBy)
        {
            html.Append($"<tr><td style=\"padding:16px 24px;font-size:12px;color:#52525B;border-top:1px solid #E4E4E7;\"><a href=\"{EmailBrandingOptions.PoweredByUrl}\" style=\"color:#52525B;\">{PoweredByText}</a></td></tr>");
        }

        html.Append("</table></td></tr></table></body></html>");
        return html.ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}

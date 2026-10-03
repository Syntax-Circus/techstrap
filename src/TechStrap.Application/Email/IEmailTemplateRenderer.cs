namespace TechStrap.Application.Email;

public interface IEmailTemplateRenderer
{
    RenderedEmail RenderTicketConfirmation(TicketConfirmationEmail model, EmailBranding branding);
}

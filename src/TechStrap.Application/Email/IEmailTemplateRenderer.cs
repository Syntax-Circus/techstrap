namespace TechStrap.Application.Email;

public interface IEmailTemplateRenderer
{
    RenderedEmail RenderTicketConfirmation(TicketConfirmationEmail model, EmailBranding branding);

    /// <param name="messageHtml">The stored, already-sanitised message body.</param>
    RenderedEmail RenderAgentReply(AgentReplyEmail model, string messageHtml, EmailBranding branding);

    RenderedEmail RenderTicketSolved(TicketSolvedEmail model, EmailBranding branding);

    RenderedEmail RenderTicketAssigned(TicketAssignedEmail model, EmailBranding branding);

    RenderedEmail RenderNewTicketAlert(NewTicketAlertEmail model, EmailBranding branding);

    RenderedEmail RenderCustomerReplyAlert(CustomerReplyAlertEmail model, EmailBranding branding);

    RenderedEmail RenderAccessLinks(AccessLinksEmail model, EmailBranding branding);
}

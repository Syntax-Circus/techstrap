namespace TechStrap.Api.Controllers;

/// <summary>Multipart form posted by the portal contact form. Website is the honeypot: real people never see or fill it.</summary>
public sealed class PublicSubmitTicketForm
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public string? Website { get; set; }
    public List<IFormFile>? Attachments { get; set; }
}

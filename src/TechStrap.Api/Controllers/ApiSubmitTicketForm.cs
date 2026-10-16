namespace TechStrap.Api.Controllers;

/// <summary>Multipart fields for API-key intake. Metadata is a JSON string dictionary.</summary>
public sealed class ApiSubmitTicketForm
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public string? ExternalUserRef { get; set; }
    public string? Metadata { get; set; }
    public string? Type { get; set; }
    public List<IFormFile>? Attachments { get; set; }
}

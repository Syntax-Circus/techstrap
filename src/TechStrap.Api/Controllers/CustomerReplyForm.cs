namespace TechStrap.Api.Controllers;

/// <summary>Multipart form posted by a customer reply: the plain-text body and optional files.</summary>
public sealed class CustomerReplyForm
{
    public string? Body { get; set; }
    public List<IFormFile>? Attachments { get; set; }
}

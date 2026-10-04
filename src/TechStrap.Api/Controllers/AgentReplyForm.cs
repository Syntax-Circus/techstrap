namespace TechStrap.Api.Controllers;

/// <summary>Multipart form posted by an agent reply: the Markdown body, optional linked articles, files and the status to set after sending.</summary>
public sealed class AgentReplyForm
{
    public string? Body { get; set; }
    public List<Guid>? LinkedArticleIds { get; set; }
    public string? StatusAfter { get; set; }
    public uint? RowVersion { get; set; }
    public List<IFormFile>? Attachments { get; set; }
}

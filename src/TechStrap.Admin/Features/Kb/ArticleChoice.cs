namespace TechStrap.Admin.Features.Kb;

/// <summary>An article an agent may link from a reply: the id the reply sends, the title the chip shows, and whether it is shared by every product.</summary>
public sealed record ArticleChoice(Guid Id, string Title, bool IsShared);

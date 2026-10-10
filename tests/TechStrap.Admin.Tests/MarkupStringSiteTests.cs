using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The Admin turns API HTML into markup in exactly two places, and each shows HTML the server sanitized: the message bubble (a ticket message body, PHASE-07 T10) and the knowledge base preview pane
/// (the answer of <c>POST /api/kb/preview</c>, which runs the same renderer and sanitizer as the portal, PHASE-08 T16). A third place must be argued for in the same commit that adds it.
/// </summary>
public sealed class MarkupStringSiteTests
{
    [Fact]
    public void MarkupString_is_used_in_exactly_two_files_the_message_bubble_and_the_kb_preview_pane()
    {
        var admin = RepositoryRoot.Combine("src", "TechStrap.Admin");
        var separator = Path.DirectorySeparatorChar;

        var users = Directory.EnumerateFiles(admin, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".razor", StringComparison.Ordinal) || file.EndsWith(".cs", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{separator}obj{separator}") && !file.Contains($"{separator}bin{separator}"))
            .Where(file => File.ReadAllText(file).Contains("MarkupString", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(admin, file).Replace('\\', '/'))
            .ToList();

        users.Order(StringComparer.Ordinal).ShouldBe(["Features/Kb/KbPreviewPane.razor", "Features/Tickets/MessageBubble.razor"]);
    }
}

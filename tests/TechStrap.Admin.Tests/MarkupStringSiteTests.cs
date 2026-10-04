using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The one place the Admin turns API HTML into markup is the message bubble; the server sanitises that body (PHASE-07 T10).</summary>
public sealed class MarkupStringSiteTests
{
    [Fact]
    public void MarkupString_is_used_in_exactly_one_file_the_message_bubble()
    {
        var admin = RepositoryRoot.Combine("src", "TechStrap.Admin");
        var separator = Path.DirectorySeparatorChar;

        var users = Directory.EnumerateFiles(admin, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".razor", StringComparison.Ordinal) || file.EndsWith(".cs", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{separator}obj{separator}") && !file.Contains($"{separator}bin{separator}"))
            .Where(file => File.ReadAllText(file).Contains("MarkupString", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(admin, file).Replace('\\', '/'))
            .ToList();

        users.ShouldBe(["Features/Tickets/MessageBubble.razor"]);
    }
}

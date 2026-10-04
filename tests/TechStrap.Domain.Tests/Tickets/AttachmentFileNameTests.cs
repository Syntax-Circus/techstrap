using System.Text;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Tickets;

public sealed class AttachmentFileNameTests
{
    [Theory]
    [InlineData("shot.png", "shot.png")]
    [InlineData("C:\\fakepath\\shot.png", "shot.png")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("  spaced.png  ", "spaced.png")]
    [InlineData("a\u0007b\u200Ec.png", "abc.png")]          // control and Format characters are dropped
    [InlineData("", "attachment")]
    [InlineData(null, "attachment")]
    [InlineData(".", "attachment")]
    [InlineData("..", "attachment")]
    [InlineData("folder/", "attachment")]
    public void Names_are_reduced_to_a_safe_display_name(string? input, string expected) =>
        AttachmentFileName.Sanitize(input).ShouldBe(expected);

    [Fact]
    public void A_long_name_keeps_its_extension_and_is_never_cut_inside_a_surrogate_pair()
    {
        var name = new string('a', 250) + "😀" + new string('b', 10) + ".png";

        var sanitised = AttachmentFileName.Sanitize(name);

        sanitised.Length.ShouldBeLessThanOrEqualTo(DomainLimits.FileNameMaxLength);
        sanitised.ShouldEndWith(".png");
        Should.NotThrow(() => new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(sanitised));
    }

    [Fact]
    public void A_name_with_a_very_long_extension_is_cut_without_one()
    {
        var name = "x" + ".".PadRight(300, 'e');

        var sanitised = AttachmentFileName.Sanitize(name);

        sanitised.Length.ShouldBeLessThanOrEqualTo(DomainLimits.FileNameMaxLength);
    }

    [Fact]
    public void Sanitising_twice_changes_nothing()
    {
        var testCases = new[]
        {
            "shot.png",
            "C:\\fakepath\\shot.png",
            "../../etc/passwd.txt",
            "  spaced.png  ",
            "a\u0007b\u200Ec.png",
            "",
            ".",
            "..",
            "folder/",
            new string('a', 250) + "😀" + new string('b', 10) + ".png",
        };

        foreach (var testCase in testCases)
        {
            var first = AttachmentFileName.Sanitize(testCase);
            var second = AttachmentFileName.Sanitize(first);
            second.ShouldBe(first, $"Sanitizing '{testCase}' twice should not change the result");
        }
    }
}

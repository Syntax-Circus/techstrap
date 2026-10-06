using System.Globalization;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// Review Focus 3: a file name comes from the visitor's browser and goes into a multipart part header. <c>MultipartFormDataContent.Add</c> throws on an empty name, and the API connection does not map an
/// <see cref="ArgumentException"/>, so whatever the browser sent must come out as a safe, non-empty name. The Portal's own copy of the Admin's rule, with one addition: a path (some browsers send the whole
/// path) is cut to its last segment before the separators are removed.
/// </summary>
public sealed class AttachmentFileNameTests
{
    private static string RightToLeftOverride => ((char)0x202E).ToString();

    private static string ZeroWidthSpace => ((char)0x200B).ToString();

    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("  report final.pdf  ", "report final.pdf")]
    [InlineData("C:\\Users\\jo\\Desktop\\report.pdf", "report.pdf")]
    [InlineData("/home/jo/report.pdf", "report.pdf")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("a\"b'c.txt", "abc.txt")]
    [InlineData("tab\there\r\n.txt", "tabhere.txt")]
    public void A_name_is_cut_to_its_last_path_segment_and_stripped_of_quotes_and_control_characters(string name, string expected) =>
        AttachmentFileName.Clean(name).ShouldBe(expected);

    [Fact]
    public void Unicode_format_characters_such_as_the_right_to_left_override_are_removed_and_the_extension_survives()
    {
        var spoofed = "invoice" + RightToLeftOverride + "fdp.exe" + ZeroWidthSpace;

        var cleaned = AttachmentFileName.Clean(spoofed);

        cleaned.ShouldBe("invoicefdp.exe");
        cleaned.ShouldAllBe(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    [InlineData("\\\\")]
    [InlineData("\"'\"")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/..")]
    public void A_name_that_ends_up_empty_or_only_dots_becomes_the_fallback(string? name) =>
        AttachmentFileName.Clean(name).ShouldBe(AttachmentFileName.Fallback);

    [Fact]
    public void The_fallback_is_a_plain_name()
    {
        AttachmentFileName.Fallback.ShouldBe("attachment");
    }
}

using TechStrap.Contracts.Intake;
using TechStrap.Portal.Forms;

namespace TechStrap.Portal.Tests.Forms;

/// <summary>
/// Review Focus 3: the files a visitor picked are checked against the API's own limits (count, size per file, total, type) before anything is sent, each failure names its file by the cleaned name, and a file is
/// opened only with the per-file limit (the framework's default of 512,000 bytes would throw on a normal screenshot).
/// </summary>
public sealed class AttachmentRulesTests
{
    private static string[] Codes(params FakeBrowserFile[] files) => [.. AttachmentRules.Validate(files).Select(e => e.Code)];

    [Fact]
    public void No_files_is_fine()
    {
        AttachmentRules.Validate(null).ShouldBeEmpty();
        AttachmentRules.Validate([]).ShouldBeEmpty();
    }

    [Fact]
    public void Five_files_of_the_biggest_size_that_together_fit_are_fine()
    {
        var files = Enumerable.Range(0, IntakeLimits.MaxFiles).Select(i => new FakeBrowserFile($"f{i}.pdf", IntakeLimits.MaxMessageBytes / IntakeLimits.MaxFiles)).ToArray();

        Codes(files).ShouldBeEmpty();
    }

    [Fact]
    public void A_sixth_file_is_too_many()
    {
        var files = Enumerable.Range(0, IntakeLimits.MaxFiles + 1).Select(i => new FakeBrowserFile($"f{i}.txt", 1)).ToArray();

        Codes(files).ShouldBe(["attachments-too-many"]);
    }

    [Fact]
    public void The_per_file_limit_is_inclusive()
    {
        Codes(new FakeBrowserFile("ok.zip", IntakeLimits.MaxFileBytes)).ShouldBeEmpty();
        var error = AttachmentRules.Validate([new FakeBrowserFile("big report.zip", IntakeLimits.MaxFileBytes + 1)]).ShouldHaveSingleItem();

        error.Code.ShouldBe("attachment-too-large");
        error.Field.ShouldBe(FormFields.Attachments);
        error.Message.ShouldBe("big report.zip is over 10 MB. Send a smaller file.");
    }

    [Fact]
    public void An_empty_file_is_named()
    {
        var error = AttachmentRules.Validate([new FakeBrowserFile("empty.txt", 0)]).ShouldHaveSingleItem();

        error.Code.ShouldBe("attachment-empty");
        error.Message.ShouldBe("empty.txt is empty. Remove it or choose another.");
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("noextension")]
    [InlineData("archive.tar.gz")]
    [InlineData("photo.png.exe")]
    [InlineData("script.html")]
    public void A_type_that_is_not_on_the_list_is_refused_and_named(string name)
    {
        var error = AttachmentRules.Validate([new FakeBrowserFile(name, 5)]).ShouldHaveSingleItem();

        error.Code.ShouldBe("attachment-type-not-allowed");
        error.Message.ShouldStartWith($"{name} is a type we cannot accept.");
        error.Message.ShouldContain(".png, .jpg");
    }

    [Theory]
    [InlineData("photo.PNG")]
    [InlineData("Photo.JpEg")]
    [InlineData("notes.TXT")]
    [InlineData("data.csv")]
    [InlineData("server.log")]
    [InlineData("bundle.zip")]
    public void Every_allowed_type_is_accepted_whatever_its_case(string name)
    {
        Codes(new FakeBrowserFile(name, 5)).ShouldBeEmpty();
    }

    [Fact]
    public void Together_the_files_may_not_pass_the_message_limit_even_when_each_is_fine()
    {
        var third = IntakeLimits.MaxMessageBytes / 3 + 1;

        Codes(new FakeBrowserFile("a.zip", third), new FakeBrowserFile("b.zip", third), new FakeBrowserFile("c.zip", third)).ShouldBe(["attachments-too-large"]);
    }

    [Fact]
    public void A_path_in_the_name_is_cut_to_the_file_name_in_the_sentence()
    {
        var error = AttachmentRules.Validate([new FakeBrowserFile("C:\\Users\\jo\\Desktop\\virus.exe", 5)]).ShouldHaveSingleItem();

        error.Message.ShouldStartWith("virus.exe is a type");
        error.Message.ShouldNotContain("Users");
    }

    [Fact]
    public void Every_failing_file_is_reported_not_only_the_first()
    {
        Codes(new FakeBrowserFile("a.exe", 5), new FakeBrowserFile("b.txt", 0), new FakeBrowserFile("c.zip", IntakeLimits.MaxFileBytes + 1))
            .ShouldBe(["attachment-type-not-allowed", "attachment-empty", "attachment-too-large"]);
    }

    [Fact]
    public async Task A_file_is_opened_only_when_the_upload_is_read_and_always_with_the_per_file_limit()
    {
        var file = new FakeBrowserFile("shot.png", 600_000, "image/png");
        var uploads = AttachmentRules.ToUploads([file]);

        file.Opened.ShouldBe(0, "nothing is opened until the request is built");
        await using var stream = uploads.ShouldHaveSingleItem().OpenRead();

        file.Opened.ShouldBe(1);
        file.LastLimit.ShouldBe(IntakeLimits.MaxFileBytes, "the framework's own default of 512,000 bytes would have thrown on this 600,000 byte file");
        stream.Length.ShouldBe(600_000);
    }

    [Fact]
    public void An_upload_carries_the_browsers_name_and_content_type_for_the_client_to_clean()
    {
        var upload = AttachmentRules.ToUploads([new FakeBrowserFile("C:\\x\\a.txt", 1, "text/plain")]).ShouldHaveSingleItem();

        upload.FileName.ShouldBe("C:\\x\\a.txt");
        upload.ContentType.ShouldBe("text/plain");
        AttachmentRules.ToUploads(null).ShouldBeEmpty();
    }
}

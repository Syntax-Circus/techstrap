using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// The Portal's multipart body for a new ticket and for a reply. The text fields come first, each file is a part named <c>Attachments</c> with its cleaned name and its own content type (octet-stream when the
/// browser's is unusable), the form owns the file streams, and a failure half way through opens nothing it does not close.
/// </summary>
public sealed class MultipartFormTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Closed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }

    private static AttachmentUpload Upload(string name, string? type, Stream stream) => new(name, type, () => stream);

    [Fact]
    public async Task Text_fields_and_files_become_parts_with_the_api_field_names()
    {
        using var form = MultipartForm.Build(
            [new("Email", "ada@example.com"), new("Subject", "Help")],
            [Upload("log.txt", "text/plain", new MemoryStream("hello"u8.ToArray()))]);

        var text = await form.ReadAsStringAsync(Ct);

        text.ShouldContain("name=Email");
        text.ShouldContain("ada@example.com");
        text.ShouldContain("name=Subject");
        text.ShouldContain("name=Attachments; filename=log.txt");
        text.ShouldContain("Content-Type: text/plain");
        text.ShouldContain("hello");
        text.IndexOf("name=Subject", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("name=Attachments", StringComparison.Ordinal), "the text fields come first");
    }

    [Fact]
    public async Task A_field_with_no_value_is_left_out()
    {
        using var form = MultipartForm.Build([new("Name", "Ada"), new("Website", null)], []);

        var text = await form.ReadAsStringAsync(Ct);

        text.ShouldContain("name=Name");
        text.ShouldNotContain("name=Website");
    }

    [Fact]
    public async Task A_file_name_is_cleaned_and_an_unusable_content_type_is_octet_stream()
    {
        using var form = MultipartForm.Build([], [Upload("C:\\fakepath\\a\"b.bin", "not a type", new MemoryStream([1, 2, 3]))]);

        var text = await form.ReadAsStringAsync(Ct);

        text.ShouldContain("filename=ab.bin");
        text.ShouldNotContain("fakepath");
        text.ShouldContain("Content-Type: application/octet-stream");
    }

    [Fact]
    public void A_missing_content_type_is_octet_stream_too()
    {
        using var form = MultipartForm.Build([], [Upload("a.bin", null, new MemoryStream([1]))]);

        form.ShouldHaveSingleItem().Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");
    }

    [Fact]
    public void An_empty_name_never_reaches_the_multipart_content_which_would_throw_on_it()
    {
        var build = () => MultipartForm.Build([], [Upload(string.Empty, "text/plain", new MemoryStream([1]))]);

        using var form = build();

        form.ShouldHaveSingleItem().Headers.ContentDisposition!.FileName.ShouldBe(AttachmentFileName.Fallback);
    }

    [Fact]
    public void Disposing_the_form_closes_the_file_streams()
    {
        var stream = new TrackingStream([1, 2, 3]);
        var form = MultipartForm.Build([], [Upload("a.bin", "application/octet-stream", stream)]);

        stream.Closed.ShouldBeFalse("the stream stays open until the request has been sent");
        form.Dispose();

        stream.Closed.ShouldBeTrue();
    }

    [Fact]
    public void A_file_that_cannot_be_opened_disposes_the_streams_already_opened_and_the_error_surfaces()
    {
        var first = new TrackingStream([1]);
        var build = () => MultipartForm.Build([], [Upload("a.bin", null, first), new AttachmentUpload("b.bin", null, () => throw new IOException("gone"))]);

        Should.Throw<IOException>(build);

        first.Closed.ShouldBeTrue();
    }

    [Fact]
    public void Every_part_has_its_own_content_type_value()
    {
        using var form = MultipartForm.Build([], [Upload("a.bin", "not a type", new MemoryStream([1])), Upload("b.bin", "also not a type", new MemoryStream([2]))]);

        var types = form.Select(part => part.Headers.ContentType).ToList();

        types.Count.ShouldBe(2);
        types[0].ShouldNotBeNull().MediaType.ShouldBe("application/octet-stream");
        types[1].ShouldNotBeNull().MediaType.ShouldBe("application/octet-stream");
        ReferenceEquals(types[0], types[1]).ShouldBeFalse("a header value is mutable, so two parts must not share one");
    }
}

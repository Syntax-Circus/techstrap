using System.Text;
using Microsoft.Extensions.Options;
using SyntaxCircus.Storage;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;
using TechStrap.Infrastructure.Attachments;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Review Focus 2 (image upload abuse), against the real Local storage provider: polyglot and SVG uploads, a spoofed content type, an oversize file and a
/// path-traversal name are all refused, and the stored key is the store's own.
/// </summary>
public sealed class KbImageStoreTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly byte[] Png = [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }, .. new byte[40]];
    private static readonly byte[] Jpeg = [.. new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0 }, .. new byte[40]];
    private static readonly byte[] Gif = [.. "GIF89a"u8.ToArray(), .. new byte[20]];
    private static readonly byte[] Webp = [.. "RIFF"u8.ToArray(), 0x24, 0, 0, 0, .. "WEBPVP8 "u8.ToArray(), .. new byte[20]];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "techstrap-kbimages-" + Guid.NewGuid().ToString("N"));
    private readonly KbImageStore _store;

    public KbImageStoreTests() =>
        _store = new KbImageStore(new LocalFileStorageProvider(Options.Create(new LocalStorageOptions { RootPath = _root })));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static IncomingKbImage Upload(byte[] bytes, long? declared = null) => new(declared ?? bytes.Length, new MemoryStream(bytes));

    public static TheoryData<string, byte[], string, string> Accepted() => new()
    {
        { "png", Png, "image/png", "png" },
        { "jpeg", Jpeg, "image/jpeg", "jpg" },
        { "gif", Gif, "image/gif", "gif" },
        { "webp", Webp, "image/webp", "webp" },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public async Task A_png_jpeg_gif_or_webp_is_stored_under_a_random_kb_images_key_with_its_own_extension_and_reads_back(string label, byte[] bytes, string contentType, string extension)
    {
        var stored = (await _store.SaveAsync(Upload(bytes), Ct)).Value;

        stored.ContentType.ShouldBe(contentType, label);
        stored.Size.ShouldBe(bytes.Length);
        stored.FileName.ShouldMatch("^[0-9a-f]{32}\\." + extension + "$");
        stored.Key.ShouldBe("kb-images/" + stored.FileName);
        File.ReadAllBytes(Path.Combine(_root, stored.Key)).ShouldBe(bytes);
        await using var read = (await _store.OpenReadAsync(stored.FileName, Ct))!.Content;
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy, Ct);
        copy.ToArray().ShouldBe(bytes);
    }

    [Fact]
    public async Task Two_uploads_of_the_same_file_get_different_keys()
    {
        var first = (await _store.SaveAsync(Upload(Png), Ct)).Value;
        var second = (await _store.SaveAsync(Upload(Png), Ct)).Value;

        first.Key.ShouldNotBe(second.Key);
    }

    public static TheoryData<string, byte[]> Refused() => new()
    {
        { "svg", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"></svg>") },
        { "svg with an xml prolog", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>") },
        { "html", Encoding.UTF8.GetBytes("<html><body><script>alert(1)</script></body></html>") },
        { "a script", Encoding.UTF8.GetBytes("alert(1)") },
        { "an executable", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0, 0, 0] },
        { "a pdf", Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n") },
        { "a zip", [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0] },
        { "an empty file", [] },
        { "a png signature with no header chunk", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13] },
        { "a riff file that is not webp", [.. "RIFF"u8.ToArray(), 0x24, 0, 0, 0, .. "WAVEfmt "u8.ToArray(), .. new byte[20]] },
        { "a gif that is really a page", [.. "GIF89a"u8.ToArray(), .. new byte[8], .. "<script>alert(document.domain)</script>"u8.ToArray()] },
        { "a png with a script in a text chunk", [.. Png, .. "<ScRiPt>alert(1)</ScRiPt>"u8.ToArray()] },
        { "a jpeg with an svg inside", [.. Jpeg, .. "<svg onload=alert(1)>"u8.ToArray()] },
        { "a bmp", [.. "BM"u8.ToArray(), 0x3A, 0, 0, 0, 0, 0, 0, 0, 0x36, 0, 0, 0, .. new byte[40]] },
        { "a tiff (little endian)", [0x49, 0x49, 0x2A, 0x00, 0x08, 0, 0, 0, .. new byte[40]] },
        { "a tiff (big endian)", [0x4D, 0x4D, 0x00, 0x2A, 0, 0, 0, 0x08, .. new byte[40]] },
        { "an ico", [0, 0, 1, 0, 1, 0, 16, 16, 0, 0, 1, 0, 32, 0, .. new byte[40]] },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Anything_that_is_not_a_plain_png_jpeg_gif_or_webp_is_refused_and_nothing_is_stored(string label, byte[] bytes)
    {
        var result = await _store.SaveAsync(Upload(bytes), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("kb-image-type-not-allowed", label),
            error => error.Target.ShouldBe("file"));
        Directory.Exists(Path.Combine(_root, "kb-images")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_by_its_declared_length_and_by_its_real_length()
    {
        var declaredTooLarge = await _store.SaveAsync(new IncomingKbImage(KbLimits.MaxImageBytes + 1, new MemoryStream(Png)), Ct);
        var liesAboutLength = await _store.SaveAsync(new IncomingKbImage(10, new MemoryStream([.. Png, .. new byte[KbLimits.MaxImageBytes]])), Ct);

        declaredTooLarge.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-image-too-large");
        liesAboutLength.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-image-too-large");
        Directory.Exists(Path.Combine(_root, "kb-images")).ShouldBeFalse();
    }

    // Only the leading window is scanned (what a browser content-sniffs). Compressed image data is near-random and would otherwise hit a marker by chance.
    private static byte[] HighEntropyPng(int length, int markerAt, byte[]? marker)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        Png.CopyTo(bytes, 0);
        for (var i = Png.Length; i < KbImageSignatures.MarkupScanWindow && i < length; i++)
        {
            bytes[i] = 0;
        }

        marker?.CopyTo(bytes, markerAt);
        return bytes;
    }

    [Fact]
    public async Task A_marker_after_the_scan_window_in_a_five_megabyte_high_entropy_body_is_accepted()
    {
        var bytes = HighEntropyPng((int)KbLimits.MaxImageBytes, KbImageSignatures.MarkupScanWindow + 976, "<svg"u8.ToArray());

        (await _store.SaveAsync(Upload(bytes), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task The_same_marker_inside_the_scan_window_is_refused()
    {
        var bytes = HighEntropyPng((int)KbLimits.MaxImageBytes, 500, "<svg"u8.ToArray());

        (await _store.SaveAsync(Upload(bytes), Ct)).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-image-type-not-allowed");
    }

    [Fact]
    public async Task A_file_of_exactly_the_limit_is_accepted()
    {
        var bytes = new byte[KbLimits.MaxImageBytes];
        Png.CopyTo(bytes, 0);

        (await _store.SaveAsync(Upload(bytes), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("..%2F..%2Fsecret.png")]
    [InlineData("kb-images/0123456789abcdef0123456789abcdef.png")]
    [InlineData("/etc/passwd")]
    [InlineData("a/../0123456789abcdef0123456789abcdef.png")]
    [InlineData("0123456789abcdef0123456789abcdef.png/..")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.png")]
    [InlineData("0123456789abcdef0123456789abcdef.svg")]
    [InlineData("0123456789abcdef0123456789abcdef.png ")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcde.png")]
    [InlineData("0123456789abcdef0123456789abcdef.png\0.svg")]
    [InlineData("")]
    public async Task A_name_the_store_could_not_have_written_is_never_opened(string name)
    {
        Directory.CreateDirectory(Path.Combine(_root, "attachments"));
        File.WriteAllBytes(Path.Combine(_root, "secret.png"), Png);
        File.WriteAllBytes(Path.Combine(_root, "appsettings.json"), "{}"u8.ToArray());

        (await _store.OpenReadAsync(name, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_well_formed_name_that_is_not_stored_is_null()
    {
        (await _store.OpenReadAsync("0123456789abcdef0123456789abcdef.png", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task An_attachment_cannot_be_reached_through_the_image_reader()
    {
        var storage = new LocalFileStorageProvider(Options.Create(new LocalStorageOptions { RootPath = _root }));
        await storage.StoreAsync(new StoreObjectRequest("attachments/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef.png", new MemoryStream(Png), "image/png"), Ct);

        (await _store.OpenReadAsync("attachments/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef.png", Ct)).ShouldBeNull();
    }
}

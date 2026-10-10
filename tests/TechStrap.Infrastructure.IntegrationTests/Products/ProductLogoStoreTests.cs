using Microsoft.Extensions.Options;
using SyntaxCircus.Storage;
using TechStrap.Application.Products;
using TechStrap.Contracts.Products;
using TechStrap.Infrastructure.Attachments;
using TechStrap.Tests.Shared;

namespace TechStrap.Infrastructure.IntegrationTests.Products;

/// <summary>The product logo store (D-052) against the real Local storage provider: png, jpeg and webp only, 1 MiB at most, a random name, and only names the store wrote are ever read.</summary>
public sealed class ProductLogoStoreTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly byte[] Png = [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }, .. new byte[40]];
    private static readonly byte[] Jpeg = [.. new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0 }, .. new byte[40]];
    private static readonly byte[] Gif = [.. "GIF89a"u8.ToArray(), .. new byte[20]];
    private static readonly byte[] Webp = [.. "RIFF"u8.ToArray(), 0x24, 0, 0, 0, .. "WEBPVP8 "u8.ToArray(), .. new byte[20]];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "techstrap-productlogos-" + Guid.NewGuid().ToString("N"));
    private readonly ProductLogoStore _store;

    public ProductLogoStoreTests() =>
        _store = new ProductLogoStore(new LocalFileStorageProvider(Options.Create(new LocalStorageOptions { RootPath = _root })));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ProductLogoStore Store() => _store;

    private static byte[] PngOfLength(int length)
    {
        var bytes = new byte[length];
        Png.CopyTo(bytes, 0);
        return bytes;
    }

    [Fact]
    public async Task A_png_a_jpeg_and_a_webp_are_stored_under_product_logos_with_a_random_name()
    {
        foreach (var (bytes, extension, contentType) in new[] { (Png, "png", "image/png"), (Jpeg, "jpg", "image/jpeg"), (Webp, "webp", "image/webp") })
        {
            var stored = (await Store().SaveAsync(new IncomingProductLogo(bytes.Length, new MemoryStream(bytes)), Ct)).Value;

            stored.Key.ShouldStartWith("product-logos/");
            stored.FileName.ShouldEndWith("." + extension);
            ProductLogoName.IsValid(stored.FileName).ShouldBeTrue();
            stored.ContentType.ShouldBe(contentType);
            File.ReadAllBytes(Path.Combine(_root, stored.Key)).ShouldBe(bytes);
            var opened = (await Store().OpenReadAsync(stored.FileName, Ct)).ShouldNotBeNull();
            await opened.Content.DisposeAsync();
            opened.ContentType.ShouldBe(contentType);
        }
    }

    [Fact]
    public async Task A_gif_an_svg_and_a_zero_byte_file_are_refused_as_type_not_allowed()
    {
        foreach (var bytes in new[] { Gif, "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), Array.Empty<byte>() })
        {
            var result = await Store().SaveAsync(new IncomingProductLogo(bytes.Length, new MemoryStream(bytes)), Ct);

            result.IsFailure.ShouldBeTrue();
            result.Errors[0].Code.ShouldBe("product-logo-type-not-allowed");
            result.Errors[0].Target.ShouldBe("file");
        }
    }

    [Fact]
    public async Task One_mebibyte_is_accepted_and_one_byte_more_is_too_large_whether_declared_or_real()
    {
        var atLimit = PngOfLength((int)ProductLogoLimits.MaxBytes);
        (await Store().SaveAsync(new IncomingProductLogo(atLimit.Length, new MemoryStream(atLimit)), Ct)).IsSuccess.ShouldBeTrue();

        var declaredOver = await Store().SaveAsync(new IncomingProductLogo(ProductLogoLimits.MaxBytes + 1, new MemoryStream(Png)), Ct);
        declaredOver.Errors[0].Code.ShouldBe("product-logo-too-large");

        var over = PngOfLength((int)ProductLogoLimits.MaxBytes + 1);
        var lyingLength = await Store().SaveAsync(new IncomingProductLogo(Png.Length, new MemoryStream(over)), Ct);
        lyingLength.Errors[0].Code.ShouldBe("product-logo-too-large");
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.gif")]
    [InlineData("../kb-images/0123456789abcdef0123456789abcdef.png")]
    [InlineData("0123456789ABCDEF0123456789abcdef.png")]
    public async Task A_name_the_store_could_not_have_written_is_never_read(string name) =>
        (await Store().OpenReadAsync(name, Ct)).ShouldBeNull();

    [Fact]
    public async Task Delete_removes_the_object_and_is_quiet_for_a_missing_or_invalid_name()
    {
        var stored = (await Store().SaveAsync(new IncomingProductLogo(Png.Length, new MemoryStream(Png)), Ct)).Value;

        await Store().DeleteAsync(stored.FileName, Ct);
        await Store().DeleteAsync(stored.FileName, Ct);
        await Store().DeleteAsync("../etc/passwd", Ct);

        (await Store().OpenReadAsync(stored.FileName, Ct)).ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(HostileUploadCorpus.Rows), MemberType = typeof(HostileUploadCorpus))]
    public async Task Every_hostile_upload_gets_its_recorded_product_logo_outcome(string id)
    {
        var upload = HostileUploadCorpus.Get(id);

        var result = await Store().SaveAsync(new IncomingProductLogo(upload.Content.Length, new MemoryStream(upload.Content)), Ct);

        result.IsSuccess.ShouldBe(upload.ProductLogo.Stored, id);
        if (!upload.ProductLogo.Stored)
        {
            result.Errors[0].Code.ShouldBe(upload.ProductLogo.ErrorCode, id);
        }
    }
}

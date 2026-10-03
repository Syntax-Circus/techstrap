using System.Text;
using Microsoft.Extensions.Options;
using SyntaxCircus.Storage;
using TechStrap.Application.Attachments;
using TechStrap.Infrastructure.Attachments;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class AttachmentStoreTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n");
    private static readonly byte[] Exe = [0x4D, 0x5A, 0x90, 0x00, 0x03];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "techstrap-attachments-" + Guid.NewGuid().ToString("N"));
    private readonly AttachmentStore _store;

    public AttachmentStoreTests() =>
        _store = new AttachmentStore(new LocalFileStorageProvider(Options.Create(new LocalStorageOptions { RootPath = _root })));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static IncomingAttachment Upload(string name, string? type, byte[] bytes) => new(name, type, bytes.Length, new MemoryStream(bytes));

    [Fact]
    public async Task A_png_is_stored_under_a_random_key_with_its_canonical_type_and_reads_back()
    {
        var ticketId = Guid.CreateVersion7();

        var stored = (await _store.SaveAsync(ticketId, Upload("screen shot.png", "image/png", Png), TestContext.Current.CancellationToken)).Value;

        stored.ContentType.ShouldBe("image/png");
        stored.FileName.ShouldBe("screen shot.png");
        stored.StorageKey.ShouldStartWith($"attachments/{ticketId:N}/");
        stored.StorageKey.ShouldNotContain("screen");
        System.IO.File.ReadAllBytes(Path.Combine(_root, stored.StorageKey)).ShouldBe(Png);
    }

    [Fact]
    public async Task A_renamed_executable_is_rejected_by_its_leading_bytes()
    {
        var result = await _store.SaveAsync(Guid.CreateVersion7(), Upload("invoice.pdf", "application/pdf", Exe), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-type-not-allowed");
        Directory.Exists(_root).ShouldBeFalse();
    }

    [Theory]
    [InlineData("tool.exe", "application/octet-stream")]
    [InlineData("invoice.pdf.exe", "application/pdf")]
    [InlineData("page.html", "text/html")]
    public async Task Disallowed_extensions_are_rejected(string name, string type) =>
        (await _store.SaveAsync(Guid.CreateVersion7(), Upload(name, type, Pdf), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-type-not-allowed");

    [Fact]
    public async Task A_declared_type_that_does_not_match_the_content_is_rejected() =>
        (await _store.SaveAsync(Guid.CreateVersion7(), Upload("photo.png", "image/png", Pdf), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-type-not-allowed");

    [Fact]
    public async Task An_oversize_file_is_rejected_even_if_its_declared_length_lies()
    {
        var big = new byte[TechStrap.Contracts.Intake.IntakeLimits.MaxFileBytes + 1];
        Png.CopyTo(big, 0);

        var result = await _store.SaveAsync(Guid.CreateVersion7(), new IncomingAttachment("big.png", "image/png", 10, new MemoryStream(big)), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-too-large");
        (Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : []).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_path_traversal_file_name_never_escapes_the_root()
    {
        var stored = (await _store.SaveAsync(Guid.CreateVersion7(), Upload("../../etc/passwd.txt", "text/plain", "hello"u8.ToArray()), TestContext.Current.CancellationToken)).Value;

        stored.FileName.ShouldBe("passwd.txt");
        Path.GetFullPath(Path.Combine(_root, stored.StorageKey)).ShouldStartWith(Path.GetFullPath(_root));
    }

    [Fact]
    public async Task Text_with_binary_content_is_rejected() =>
        (await _store.SaveAsync(Guid.CreateVersion7(), Upload("app.log", "text/plain", [0x41, 0x00, 0x42]), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-type-not-allowed");

    [Fact]
    public async Task Delete_removes_the_file_and_is_harmless_twice()
    {
        var stored = (await _store.SaveAsync(Guid.CreateVersion7(), Upload("a.pdf", "application/pdf", Pdf), TestContext.Current.CancellationToken)).Value;

        await _store.DeleteAsync(stored.StorageKey, TestContext.Current.CancellationToken);
        await _store.DeleteAsync(stored.StorageKey, TestContext.Current.CancellationToken);

        System.IO.File.Exists(Path.Combine(_root, stored.StorageKey)).ShouldBeFalse();
    }
}

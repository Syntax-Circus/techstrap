using System.Buffers;
using System.Globalization;
using System.Text;
using SyntaxCircus.Common;
using SyntaxCircus.Storage;
using TechStrap.Application.Attachments;
using TechStrap.Contracts.Intake;
using TechStrap.Domain.Rules;

namespace TechStrap.Infrastructure.Attachments;

internal sealed class AttachmentStore(IStorageProvider storage) : IAttachmentStore
{
    private const string Target = "attachments";
    private const string FallbackName = "attachment";

    public async Task<Result<StoredAttachment>> SaveAsync(Guid ticketId, IncomingAttachment file, CancellationToken cancellationToken)
    {
        var safeName = SafeDisplayName(file.FileName);
        var extension = Path.GetExtension(safeName).ToLowerInvariant();
        if (!IntakeLimits.AllowedExtensions.Contains(extension))
        {
            return NotAllowed();
        }

        if (file.Length > IntakeLimits.MaxFileBytes)
        {
            return TooLarge();
        }

        // Read at most MaxFileBytes + 1 bytes so a declared length that lies cannot exhaust memory.
        await using var content = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        try
        {
            long total = 0;
            while (total <= IntakeLimits.MaxFileBytes)
            {
                var want = (int)Math.Min(buffer.Length, IntakeLimits.MaxFileBytes + 1 - total);
                var read = await file.Content.ReadAsync(buffer.AsMemory(0, want), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                content.Write(buffer, 0, read);
                total += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (content.Length > IntakeLimits.MaxFileBytes)
        {
            return TooLarge();
        }

        if (content.Length == 0)
        {
            return Failure("attachment-empty", "This file is empty. Attach a file that has content.");
        }

        var head = content.GetBuffer().AsSpan(0, (int)Math.Min(content.Length, FileSignatures.HeadLength));
        if (!FileSignatures.TryMatch(extension, head, out var contentType)
            || !FileSignatures.IsDeclaredTypeAcceptable(extension, file.DeclaredContentType, contentType))
        {
            return NotAllowed();
        }

        var key = $"attachments/{ticketId:N}/{Guid.CreateVersion7():N}";
        content.Position = 0;
        try
        {
            await storage.StoreAsync(new StoreObjectRequest(key, content, contentType), cancellationToken);
        }
        catch
        {
            // A failed copy can leave a partial object behind.
            await DeleteQuietlyAsync(key);
            throw;
        }

        return Result<StoredAttachment>.Success(new StoredAttachment(key, safeName, contentType, content.Length));
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => storage.DeleteAsync(storageKey, cancellationToken);

    private async Task DeleteQuietlyAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception)
        {
            // Best effort only; the original failure is the one to surface.
        }
    }

    private static string SafeDisplayName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (!char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
            {
                builder.Append(c);
            }
        }

        name = builder.ToString().Trim();
        if (name is "" or "." or "..")
        {
            return FallbackName;
        }

        if (name.Length > DomainLimits.FileNameMaxLength)
        {
            var extension = Path.GetExtension(name);
            name = extension.Length < DomainLimits.FileNameMaxLength / 2
                ? string.Concat(name.AsSpan(0, DomainLimits.FileNameMaxLength - extension.Length), extension)
                : name[..DomainLimits.FileNameMaxLength];
        }

        return name;
    }

    private static Result<StoredAttachment> TooLarge() =>
        Failure("attachment-too-large", $"This file is too large. Each file can be up to {IntakeLimits.MaxFileBytes / (1024 * 1024)} MB.");

    private static Result<StoredAttachment> NotAllowed() =>
        Failure("attachment-type-not-allowed", "This file type isn't accepted. Attach images, PDFs, text, CSV or ZIP files.");

    private static Result<StoredAttachment> Failure(string code, string message) =>
        Result<StoredAttachment>.Failure(new ResultError(code, message, ResultErrorKind.Validation, Target));
}

using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Client.Json;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client;

// Deliberately not an IOException: local source failures must not be retried as transport failures.
internal sealed class AttachmentUploadException(ResultError error) : Exception("Attachment upload failed.")
{
    public ResultError Error { get; } = error;
}

internal static class AttachmentUpload
{
    private const string Target = "attachments";
    public static ResultError ReadFailed() => new(TechStrapClientErrorCodes.AttachmentReadFailed, "An attachment could not be read. Select the file again and retry with the same submission key.", ResultErrorKind.Validation, Target);
    private static ResultError Invalid(string code, string message) => new(code, message, ResultErrorKind.Validation, Target);
    internal static ResultError TooLarge() => Invalid(TechStrapClientErrorCodes.AttachmentTooLarge, "An attachment exceeds 10 MiB.");
    internal static ResultError TotalTooLarge() => Invalid(TechStrapClientErrorCodes.AttachmentsTooLarge, "The attachments together exceed 25 MiB.");

    public static ResultError? Validate(IReadOnlyList<TicketAttachment> files)
    {
        if (files.Count > IntakeLimits.MaxFiles) return Invalid(TechStrapClientErrorCodes.AttachmentsTooMany, "Attach at most five files.");
        long total = 0;
        foreach (var file in files)
        {
            if (file is null) throw new ArgumentException("An attachment must not be null.", nameof(files));
            if (string.IsNullOrWhiteSpace(file.FileName) || file.FileName.Any(c => char.IsControl(c) || c is '/' or '\\' or '"'))
                return Invalid(TechStrapClientErrorCodes.AttachmentNameInvalid, "Use a filename without paths or control characters.");
            if (!IntakeLimits.AllowedExtensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
                return Invalid(TechStrapClientErrorCodes.AttachmentTypeNotAllowed, "That attachment extension is not allowed.");
            if (!MediaTypeHeaderValue.TryParse(file.ContentType, out _))
                return Invalid(TechStrapClientErrorCodes.AttachmentContentTypeInvalid, "Provide a valid attachment content type.");
            if (file.Length < 0) return Invalid(TechStrapClientErrorCodes.AttachmentSizeInvalid, "An attachment length must not be negative.");
            if (file.Length > IntakeLimits.MaxFileBytes) return TooLarge();
            total += file.Length ?? 0;
        }
        return total > IntakeLimits.MaxMessageBytes ? TotalTooLarge() : null;
    }

    public static HttpContent Build(SubmitTicketRequest request, IReadOnlyList<TicketAttachment> files)
    {
        var form = new MultipartFormDataContent();
        try
        {
            void Field(string name, string? value) { if (value is not null) form.Add(new StringContent(value), name); }
            Field("email", request.Email);
            Field("name", request.Name);
            Field("subject", request.Subject);
            Field("body", request.Body);
            Field("externalUserRef", request.ExternalUserRef);
            Field("type", request.Type);
            if (request.Metadata is not null)
                Field("metadata", JsonSerializer.Serialize(new Dictionary<string, string>(request.Metadata), TechStrapJsonContext.Default.DictionaryStringString));
            var budget = new UploadBudget();
            foreach (var file in files) form.Add(new FileContent(file, budget), Target, file.FileName);
            return form;
        }
        catch { form.Dispose(); throw; }
    }

    private sealed class UploadBudget { public long Bytes { get; set; } }

    private sealed class FileContent : HttpContent
    {
        private readonly TicketAttachment _file;
        private readonly UploadBudget _budget;

        public FileContent(TicketAttachment file, UploadBudget budget)
        {
            _file = file;
            _budget = budget;
            Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
        }

        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream destination, TransportContext? context, CancellationToken cancellationToken)
        {
            Stream source;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                source = await _file.OpenReadAsync(cancellationToken);
            }
            catch (Exception ex) when (IsLocalFailure(ex, cancellationToken)) { throw new AttachmentUploadException(ReadFailed()); }
            if (source is null) throw new AttachmentUploadException(ReadFailed());
            var failed = false;
            try
            {
                var buffer = ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    try
                    {
                        if (!source.CanRead || (source.CanSeek && source.Position != 0)) throw new AttachmentUploadException(ReadFailed());
                    }
                    catch (Exception ex) when (IsLocalFailure(ex, cancellationToken)) { throw new AttachmentUploadException(ReadFailed()); }
                    long bytes = 0;
                    while (true)
                    {
                        int read;
                        try { read = await source.ReadAsync(buffer.AsMemory(), cancellationToken); }
                        catch (Exception ex) when (IsLocalFailure(ex, cancellationToken)) { throw new AttachmentUploadException(ReadFailed()); }
                        if (read == 0) break;
                        bytes += read;
                        _budget.Bytes += read;
                        if (bytes > IntakeLimits.MaxFileBytes) throw new AttachmentUploadException(TooLarge());
                        if (_budget.Bytes > IntakeLimits.MaxMessageBytes) throw new AttachmentUploadException(TotalTooLarge());
                        // Destination/network failures retain their normal transport classification.
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                    if (_file.Length is { } expected && expected != bytes) throw new AttachmentUploadException(ReadFailed());
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }
            catch { failed = true; throw; }
            finally
            {
                try { await source.DisposeAsync(); }
                catch (Exception ex) when (IsLocalFailure(ex, cancellationToken))
                {
                    // A failed close must not replace cancellation, a size rejection or
                    // the original network error already unwinding.
                    if (!failed) throw new AttachmentUploadException(ReadFailed());
                }
            }
        }

        // Factories may wrap platform permissions or remote file access. Their recoverable
        // failures belong to the source, regardless of exception type; only a cancellation
        // requested by the active attempt and fatal allocation failures pass through.
        private static bool IsLocalFailure(Exception ex, CancellationToken ct) =>
            ex is not OutOfMemoryException && (ex is not OperationCanceledException || !ct.IsCancellationRequested);
    }
}

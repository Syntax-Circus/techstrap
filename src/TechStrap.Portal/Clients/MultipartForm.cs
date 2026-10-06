using System.Net.Http.Headers;

namespace TechStrap.Portal.Clients;

/// <summary>
/// The multipart body of a new ticket and of a reply: the text fields first (a field with no value is left out), then one <c>Attachments</c> part per file with its cleaned name and its own content type
/// (octet-stream when the browser's is missing or unusable). The returned content owns the file streams: disposing it closes them. If a file cannot be opened the streams already opened are closed and the
/// error surfaces, so nothing leaks.
/// </summary>
internal static class MultipartForm
{
    /// <summary>The name of the file parts, as both intake endpoints bind them.</summary>
    public const string AttachmentsField = "Attachments";

    private static readonly MediaTypeHeaderValue OctetStream = new("application/octet-stream");

    public static MultipartFormDataContent Build(IEnumerable<KeyValuePair<string, string?>> fields, IReadOnlyList<AttachmentUpload> attachments)
    {
        var form = new MultipartFormDataContent();
        try
        {
            foreach (var (name, value) in fields)
            {
                if (value is not null)
                {
                    form.Add(new StringContent(value), name);
                }
            }

            foreach (var attachment in attachments)
            {
                var file = new StreamContent(attachment.OpenRead());
                file.Headers.ContentType = MediaTypeHeaderValue.TryParse(attachment.ContentType, out var type) ? type : OctetStream;
                form.Add(file, AttachmentsField, AttachmentFileName.Clean(attachment.FileName));
            }

            return form;
        }
        catch
        {
            form.Dispose();
            throw;
        }
    }
}

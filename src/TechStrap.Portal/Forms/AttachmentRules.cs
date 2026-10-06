using Microsoft.AspNetCore.Components.Forms;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Forms;

/// <summary>
/// The Portal's check of the files a visitor picked, run before anything is sent (Review Focus 3): the count, each file's size and type, and the total, from the same <see cref="IntakeLimits"/> the API enforces.
/// It spares a round trip and names the file at fault; the API is still the authority and checks again. A file name comes from the visitor's browser, so every sentence uses the cleaned name
/// (<see cref="AttachmentFileName"/>) and the page encodes it. The checks use the size the browser declared; <see cref="ToUploads"/> opens each file with the per-file limit, so a body that is larger than it
/// said cannot be read past it.
/// </summary>
public static class AttachmentRules
{
    public static IReadOnlyList<FormError> Validate(IReadOnlyList<IBrowserFile>? files)
    {
        var errors = new List<FormError>();
        if (files is not { Count: > 0 })
        {
            return errors;
        }

        if (files.Count > IntakeLimits.MaxFiles)
        {
            errors.Add(new FormError(FormFields.Attachments, "attachments-too-many", FormCopy.For("attachments-too-many")));
        }

        foreach (var file in files)
        {
            var name = AttachmentFileName.Clean(file.Name);
            if (file.Size == 0)
            {
                errors.Add(new FormError(FormFields.Attachments, "attachment-empty", FormCopy.Empty(name)));
            }
            else if (file.Size > IntakeLimits.MaxFileBytes)
            {
                errors.Add(new FormError(FormFields.Attachments, "attachment-too-large", FormCopy.TooLarge(name)));
            }

            if (!IsAllowedType(name))
            {
                errors.Add(new FormError(FormFields.Attachments, "attachment-type-not-allowed", FormCopy.TypeNotAllowed(name)));
            }
        }

        if (files.Sum(file => file.Size) > IntakeLimits.MaxMessageBytes)
        {
            errors.Add(new FormError(FormFields.Attachments, "attachments-too-large", FormCopy.For("attachments-too-large")));
        }

        return errors;
    }

    /// <summary>The files as uploads for a client. Each is opened only when the request is built, with the per-file limit (without it the framework reads at most 512,000 bytes and throws).</summary>
    public static IReadOnlyList<AttachmentUpload> ToUploads(IReadOnlyList<IBrowserFile>? files) =>
        [.. (files ?? []).Select(file => new AttachmentUpload(file.Name, file.ContentType, () => file.OpenReadStream(IntakeLimits.MaxFileBytes)))];

    private static bool IsAllowedType(string cleanedName) =>
        IntakeLimits.AllowedExtensions.Contains(Path.GetExtension(cleanedName), StringComparer.OrdinalIgnoreCase);
}

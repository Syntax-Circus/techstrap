using Microsoft.AspNetCore.Components.Forms;
using TechStrap.Contracts.Intake;

namespace TechStrap.Portal.Forms;

/// <summary>What the reply form on the ticket page binds: the text and the files (inputs <c>Reply.Body</c> and <c>Reply.Files</c>).</summary>
public sealed class ReplyFormViewModel
{
    public string? Body { get; set; }

    public IReadOnlyList<IBrowserFile>? Files { get; set; }
}

/// <summary>The Portal's check of a reply before it asks the API: a body (judged trimmed) of at most <see cref="IntakeLimits.BodyMaxLength"/> characters, and the files against the same limits as the contact form.</summary>
public static class ReplyFormValidator
{
    public static IReadOnlyList<FormError> Validate(ReplyFormViewModel form)
    {
        var errors = new List<FormError>();
        var body = form.Body?.Trim() ?? string.Empty;
        if (body.Length == 0)
        {
            errors.Add(new FormError(FormFields.Body, "body-required", FormCopy.For("body-required")));
        }
        else if (body.Length > IntakeLimits.BodyMaxLength)
        {
            errors.Add(new FormError(FormFields.Body, "body-too-long", FormCopy.For("body-too-long")));
        }

        errors.AddRange(AttachmentRules.Validate(form.Files));
        return errors;
    }
}

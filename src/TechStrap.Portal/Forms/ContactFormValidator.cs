using System.Net.Mail;
using TechStrap.Contracts.Intake;

namespace TechStrap.Portal.Forms;

/// <summary>
/// The Portal's check of the contact form before it asks the API (the API checks again and is the authority). Every limit is a Contracts constant (<see cref="IntakeLimits"/>), the same ones the inputs'
/// <c>maxlength</c> attributes use, and the codes are the API's own, so a failure here and a failure from the API read the same (<see cref="FormCopy.For"/>). Values are judged trimmed. Name is required here
/// (the UX brief) although the API only limits its length. The honeypot is not checked: a filled one is sent on, and the API answers it.
/// </summary>
public static class ContactFormValidator
{
    public static IReadOnlyList<FormError> Validate(ContactFormViewModel form)
    {
        var errors = new List<FormError>();

        var name = form.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors.Add(Error(FormFields.Name, FormCopy.NameRequiredCode));
        }
        else if (name.Length > IntakeLimits.NameMaxLength)
        {
            errors.Add(Error(FormFields.Name, "name-too-long"));
        }

        var email = form.Email?.Trim() ?? string.Empty;
        if (email.Length == 0)
        {
            errors.Add(Error(FormFields.Email, "email-required"));
        }
        else if (email.Length > IntakeLimits.EmailMaxLength || !LooksLikeAnAddress(email))
        {
            errors.Add(Error(FormFields.Email, "email-invalid"));
        }

        var subject = form.Subject?.Trim() ?? string.Empty;
        if (subject.Length == 0)
        {
            errors.Add(Error(FormFields.Subject, "subject-required"));
        }
        else if (subject.Length > IntakeLimits.SubjectMaxLength)
        {
            errors.Add(Error(FormFields.Subject, "subject-too-long"));
        }

        var body = form.Body?.Trim() ?? string.Empty;
        if (body.Length == 0)
        {
            errors.Add(Error(FormFields.Body, "body-required"));
        }
        else if (body.Length > IntakeLimits.BodyMaxLength)
        {
            errors.Add(Error(FormFields.Body, "body-too-long"));
        }

        errors.AddRange(AttachmentRules.Validate(form.Files));
        return errors;
    }

    private static FormError Error(string field, string code) => new(field, code, FormCopy.For(code));

    // Not a full address grammar (the API decides): one address, no display name, a dotted domain.
    private static bool LooksLikeAnAddress(string text) =>
        MailAddress.TryCreate(text, out var address) && address.Address == text && address.Host.Contains('.', StringComparison.Ordinal);
}

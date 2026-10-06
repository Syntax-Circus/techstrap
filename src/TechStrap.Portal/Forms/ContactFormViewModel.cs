using Microsoft.AspNetCore.Components.Forms;

namespace TechStrap.Portal.Forms;

/// <summary>
/// What the contact form binds (D-045 addendum): the five text fields and the files, as a visitor typed or picked them. It is a plain class with settable properties because the static-SSR form binder fills it
/// from the post (the inputs are named <c>Form.Email</c> and so on). <see cref="Website"/> is the honeypot: a field no person sees, which the Portal sends to the API as it arrives. The files are the browser's
/// own <see cref="IBrowserFile"/>s (a part with no file chosen is not bound at all).
/// </summary>
public sealed class ContactFormViewModel
{
    public string? Name { get; set; }

    public string? Email { get; set; }

    public string? Subject { get; set; }

    public string? Body { get; set; }

    public string? Website { get; set; }

    public IReadOnlyList<IBrowserFile>? Files { get; set; }

    /// <summary>
    /// The form as the page opens it from <c>?subject=&amp;name=&amp;email=</c> (P09-T21): exactly these three, as typed text in the visible, editable inputs, never in a hidden field. They are checked on post
    /// like any typed value (same rules, same limits), so a prefill can be neither longer nor stranger than what a person could type.
    /// </summary>
    public static ContactFormViewModel FromPrefill(string? subject, string? name, string? email) => new() { Subject = subject, Name = name, Email = email };
}

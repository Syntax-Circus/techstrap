namespace TechStrap.Portal.Forms;

/// <summary>What the lost-link form binds: one address (input <c>Form.Email</c>).</summary>
public sealed class LostLinkFormViewModel
{
    public string? Email { get; set; }

    /// <summary>The one-time id of the form (<see cref="SubmitIds"/>), posted in a hidden input; missing or malformed means the post is not guarded.</summary>
    public string? SubmitId { get; set; }
}

/// <summary>The words of the lost-link page (UX brief). The confirmation is one sentence that says nothing about whether the address matched: it is the same for every well-formed address.</summary>
public static class LostLinkCopy
{
    public const string Heading = "Lost your ticket link?";
    public const string Intro = "Enter the email address you used when you contacted us. If we have tickets for that address, we will send you a new link.";
    public const string EmailLabel = "Email address";
    public const string Submit = "Send me a new link";
    public const string RequiredNote = "The email address is required.";
    public const string Confirmation = "If we have tickets for that address, we have sent a new link. Check your inbox and your spam folder.";

    public static string Title(string productName) => $"New ticket link: {productName}";

    public static string BackTo(string productName) => $"Back to {productName}";
}

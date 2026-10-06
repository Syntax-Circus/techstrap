using System.Globalization;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Forms;

/// <summary>
/// The words the Portal's forms share (the contact form and the reply form): plain, human, blame-free, saying what happened, what is kept and what to do (UX brief). <see cref="For"/> maps every code the API
/// can send for a ticket or a reply to a sentence of the Portal's own, so the API's wording never reaches a visitor; an unknown code gets <see cref="ProblemCopy.Invalid"/>.
/// </summary>
public static class FormCopy
{
    private const long Mebibyte = 1024 * 1024;

    public const string SummaryHeading = "Please check the following";
    public const string AttachmentsLabel = "Attachments (optional)";
    public const string AttachmentsKept = "If anything goes wrong, files are not kept: choose them again.";

    // The codes the Portal itself adds before it asks the API.
    public const string NameRequiredCode = "name-required";

    public const string RateLimited = "Too many attempts. Wait a few minutes and try again. What you wrote is still here.";
    public const string Unavailable = "We could not send that just now. What you wrote is still here: try again in a moment.";

    /// <summary>The rule for attachments, stated before anyone picks a file (UX brief): size, count and type, from the same limits the API enforces.</summary>
    public static string AttachmentRules { get; } =
        $"Up to {IntakeLimits.MaxFiles} files: images and documents of {IntakeLimits.MaxFileBytes / Mebibyte} MB each and {IntakeLimits.MaxMessageBytes / Mebibyte} MB in all ({string.Join(", ", IntakeLimits.AllowedExtensions)}).";

    /// <summary>The value of the file input's <c>accept</c> attribute: a hint to the file picker, never the check (the API decides).</summary>
    public static string Accept { get; } = string.Join(",", IntakeLimits.AllowedExtensions);

    public static string For(string code) => code switch
    {
        NameRequiredCode => "Enter your name.",
        "name-too-long" => Invariant($"Your name must be at most {IntakeLimits.NameMaxLength} characters."),
        "email-required" => "Enter your email address.",
        "email-invalid" => "Enter a valid email address, like name@example.com.",
        "subject-required" => "Enter a subject.",
        "subject-too-long" => Invariant($"The subject must be at most {IntakeLimits.SubjectMaxLength} characters."),
        "body-required" => "Write a message.",
        "body-too-long" => Invariant($"The message must be at most {IntakeLimits.BodyMaxLength:N0} characters."),
        "attachments-too-many" => Invariant($"Attach at most {IntakeLimits.MaxFiles} files."),
        "attachments-too-large" => Invariant($"Your files add up to more than {IntakeLimits.MaxMessageBytes / Mebibyte} MB. Remove a file or send smaller ones."),
        "attachment-too-large" => Invariant($"A file is over {IntakeLimits.MaxFileBytes / Mebibyte} MB. Send a smaller one."),
        "attachment-empty" => "A file is empty. Remove it or choose another.",
        "attachment-type-not-allowed" => "One of the files is a type we cannot accept. " + AttachmentRules,
        ApiErrorCodes.PayloadTooLarge => ProblemCopy.PayloadTooLarge,
        ApiErrorCodes.UnsupportedMediaType => ProblemCopy.UnsupportedMediaType,
        _ => ProblemCopy.Invalid,
    };

    // The sentences that name a file, for the checks the Portal runs before it sends anything (the visitor learns which file, UX brief). The name is the cleaned one; the page encodes it.
    public static string TooLarge(string fileName) => Invariant($"{fileName} is over {IntakeLimits.MaxFileBytes / Mebibyte} MB. Send a smaller file.");

    public static string Empty(string fileName) => $"{fileName} is empty. Remove it or choose another.";

    public static string TypeNotAllowed(string fileName) => $"{fileName} is a type we cannot accept. {AttachmentRules}";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

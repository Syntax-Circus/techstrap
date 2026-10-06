namespace TechStrap.Portal.Forms;

/// <summary>The input ids of the Portal's forms, one per field. They are also what an error summary links to (<c>#email</c>) and what an input's <c>aria-describedby</c> is built from (<c>email-error</c>).</summary>
public static class FormFields
{
    public const string Name = "name";
    public const string Email = "email";
    public const string Subject = "subject";
    public const string Body = "body";
    public const string Attachments = "attachments";

    /// <summary>The id of the error paragraph that belongs to a field.</summary>
    public static string ErrorId(string field) => $"{field}-error";

    /// <summary>The field an API validation target belongs to (<c>email</c>, <c>attachments</c> and so on, case-insensitive); null for anything else, which the summary shows without a link.</summary>
    public static string? FromTarget(string? target) => target?.Trim().ToLowerInvariant() switch
    {
        Name => Name,
        Email => Email,
        Subject => Subject,
        Body => Body,
        Attachments => Attachments,
        _ => null,
    };
}

/// <summary>
/// One thing a visitor must fix on a form: the field it is about (null for the whole form), the machine code (the API's own codes, such as <c>email-invalid</c>, plus the few the Portal adds before it asks
/// the API) and the sentence to show. The sentence is always the Portal's own copy (<see cref="FormCopy"/>), never the API's text.
/// </summary>
public sealed record FormError(string? Field, string Code, string Message);

using System.Text.RegularExpressions;

namespace TechStrap.Domain.Rules;

/// <summary>Small validation helpers. Every failure is a Validation <see cref="DomainError"/> whose code is "{target}-..." .</summary>
internal static partial class Guard
{
    private const string SlugPattern = "^[a-z0-9]+(?:-[a-z0-9]+)*$";
    private const string ColourPattern = "^#[0-9A-Fa-f]{6}$";
    private const string EmailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    /// <summary>The first failure among the given results, or null when all succeeded.</summary>
    public static DomainError? FirstError(params DomainResult[] results) =>
        results.FirstOrDefault(result => result.IsFailure)?.Error;

    public static DomainResult<string> RequiredText(string? value, int maxLength, string target)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return DomainErrors.Validation($"{target}-required", $"{target} is required.", target);
        }

        return text.Length > maxLength
            ? DomainErrors.Validation($"{target}-too-long", $"{target} must be at most {maxLength} characters.", target)
            : DomainResult<string>.Ok(text);
    }

    /// <summary>Blank becomes null; a non-blank value is trimmed and length-checked.</summary>
    public static DomainResult<string?> OptionalText(string? value, int maxLength, string target)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return DomainResult<string?>.Ok(null);
        }

        return text.Length > maxLength
            ? DomainErrors.Validation($"{target}-too-long", $"{target} must be at most {maxLength} characters.", target)
            : DomainResult<string?>.Ok(text);
    }

    public static DomainResult<string> Slug(string? value, int maxLength, string target)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > maxLength || !SlugRegex().IsMatch(text))
        {
            return DomainErrors.Validation($"{target}-invalid", $"{target} must be lower-case letters, digits and single hyphens, at most {maxLength} characters.", target);
        }

        return DomainResult<string>.Ok(text);
    }

    /// <summary>Normalises "#aabbcc" to "#AABBCC".</summary>
    public static DomainResult<string> Colour(string? value, string target)
    {
        var text = value?.Trim();
        return text is not null && ColourRegex().IsMatch(text)
            ? DomainResult<string>.Ok(text.ToUpperInvariant())
            : DomainErrors.Validation($"{target}-invalid", $"{target} must be a #RRGGBB colour.", target);
    }

    /// <summary>Trims and lower-cases an address (case-insensitive identity, FR requester email).</summary>
    public static DomainResult<string> Email(string? value, string target)
    {
        var text = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text) || text.Length > DomainLimits.EmailMaxLength || !EmailRegex().IsMatch(text))
        {
            return DomainErrors.Validation($"{target}-invalid", $"{target} must be a valid email address.", target);
        }

        return DomainResult<string>.Ok(text);
    }

    public static DomainResult<string?> OptionalEmail(string? value, string target)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DomainResult<string?>.Ok(null);
        }

        var email = Email(value, target);
        return email.IsSuccess ? DomainResult<string?>.Ok(email.Value) : DomainResult<string?>.Fail(email.Error!);
    }

    [GeneratedRegex(SlugPattern, RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    [GeneratedRegex(ColourPattern, RegexOptions.CultureInvariant)]
    private static partial Regex ColourRegex();

    [GeneratedRegex(EmailPattern, RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}

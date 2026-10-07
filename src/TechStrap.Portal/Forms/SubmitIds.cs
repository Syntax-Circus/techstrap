using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace TechStrap.Portal.Forms;

/// <summary>
/// The one-time id a form carries in a hidden input (D-045 09d addendum): 128 random bits from the operating system's generator, written as 22 base64url characters. A fresh one is made every time a form is
/// rendered, so a page that shows errors after a failed post carries a new id, never the posted one. It is a capability (the guard answers a repeat of it with the first answer), so it is random, never a counter or
/// a time, and it is only ever compared by the hash of its <see cref="SubmitKey"/>.
/// </summary>
public static partial class SubmitIds
{
    /// <summary>The length of an id: 16 random bytes, base64url, no padding.</summary>
    public const int Length = 22;

    [GeneratedRegex(@"\A[A-Za-z0-9_-]{22}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    public static string New() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

    /// <summary>True for a value of exactly the shape <see cref="New"/> makes. Anything else (missing, short, long, other characters) means the post is not guarded.</summary>
    public static bool IsWellFormed(string? value) => value is not null && Shape().IsMatch(value);
}

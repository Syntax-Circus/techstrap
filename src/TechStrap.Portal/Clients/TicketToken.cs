using System.Text.RegularExpressions;

namespace TechStrap.Portal.Clients;

/// <summary>
/// The capability to call the API as one ticket's customer (the <c>/t/{token}</c> in a link). It can only be made from a 43-character base64url value, the shape of every access token, so a
/// value that reaches a request header can never carry a character that splits a header or ends a path. It never prints: <see cref="ToString"/> is a fixed marker, so a log call that formats
/// it, an exception message or a debugger line shows no secret. The value is read only where the request header is set (<see cref="ApiConnection"/>); it is never put in a URL, a body or a log.
/// </summary>
public readonly partial struct TicketToken
{
    /// <summary>The length of an access token (the PII redactor masks the same shape in logs).</summary>
    public const int Length = 43;

    private readonly string? _value;

    private TicketToken(string value) => _value = value;

    [GeneratedRegex(@"\A[A-Za-z0-9_\-]{43}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    /// <summary>
    /// The token text. Throws for <c>default</c>. Internal on purpose: a public property would be read by a serializer or a log destructurer (<c>{@Token}</c>); only <see cref="ApiConnection"/>
    /// reads it, where the request header is set.
    /// </summary>
    internal string Value => _value ?? throw new InvalidOperationException("This ticket token has no value.");

    public static bool TryParse(string? text, out TicketToken token)
    {
        if (text is not null && Shape().IsMatch(text))
        {
            token = new TicketToken(text);
            return true;
        }

        token = default;
        return false;
    }

    public override string ToString() => "[token]";
}

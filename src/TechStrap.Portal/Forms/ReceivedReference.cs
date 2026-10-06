using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace TechStrap.Portal.Forms;

/// <summary>
/// The <c>?ref=</c> value that carries a ticket number from the contact form to the "received" page (D-045 addendum): data-protected with a purpose of its own and time-limited to
/// <see cref="Lifetime"/>, so there is no cookie, no stored state and no access token in it. A value that has expired, was tampered with, was made by another key ring (a restart without a persisted ring) or is
/// not a ticket number is simply not accepted, and the page shows its generic confirmation. It carries the ticket number only: not the subject, not the address. <see cref="IDataProtectionProvider"/> has a persisted
/// key ring in production (<c>DATAPROTECTION__KEYRINGPATH</c>, which both compose files set).
/// </summary>
public sealed partial class ReceivedReference
{
    /// <summary>The data-protection purpose: another feature's protector cannot read this one's values, and a change of format becomes a new version.</summary>
    public const string Purpose = "TechStrap.Portal.ContactReceived.v1";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>A protected number is far shorter than this; a longer value is refused without being unprotected.</summary>
    public const int MaxReferenceLength = 512;

    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeProvider _clock;

    public ReceivedReference(IDataProtectionProvider provider, TimeProvider clock)
    {
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
        _clock = clock;
    }

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9\-]{0,39}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TicketNumberShape();

    public string Protect(string ticketNumber) => _protector.Protect(ticketNumber, _clock.GetUtcNow().Add(Lifetime));

    public bool TryUnprotect(string? reference, out string ticketNumber)
    {
        ticketNumber = string.Empty;
        if (string.IsNullOrEmpty(reference) || reference.Length > MaxReferenceLength)
        {
            return false;
        }

        try
        {
            var number = _protector.Unprotect(reference);
            if (!TicketNumberShape().IsMatch(number))
            {
                return false;
            }

            ticketNumber = number;
            return true;
        }
        catch (CryptographicException)
        {
            // Expired, tampered with, not base64url, or made under another key: all the same to the page.
            return false;
        }
    }
}

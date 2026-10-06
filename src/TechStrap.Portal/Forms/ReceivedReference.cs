using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace TechStrap.Portal.Forms;

/// <summary>
/// The <c>?ref=</c> value that carries a ticket number from the contact form to the "received" page (D-045 addendum): data-protected with a purpose of its own, bound to the product it was made for, and time-limited to
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

    private readonly IDataProtectionProvider _provider;
    private readonly TimeProvider _clock;

    public ReceivedReference(IDataProtectionProvider provider, TimeProvider clock)
    {
        _provider = provider;
        _clock = clock;
    }

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9\-]{0,39}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TicketNumberShape();

    /// <summary>A reference for <paramref name="ticketNumber"/>, bound to <paramref name="productKey"/>: it is accepted for that product only, so a reference cannot be carried to another product's received page.</summary>
    public string Protect(string productKey, string ticketNumber) => ProtectorFor(productKey).Protect(ticketNumber, _clock.GetUtcNow().Add(Lifetime));

    private ITimeLimitedDataProtector ProtectorFor(string productKey) => _provider.CreateProtector(Purpose, productKey.ToLowerInvariant()).ToTimeLimitedDataProtector();

    /// <summary>The ticket number in <paramref name="reference"/> when it was made for <paramref name="productKey"/> and is still good; false for anything else. The expiry is checked against the system clock inside the framework's time-limited protector, not against the injected <see cref="TimeProvider"/>, which only stamps the expiry when a reference is made.</summary>
    public bool TryUnprotect(string productKey, string? reference, out string ticketNumber)
    {
        ticketNumber = string.Empty;
        if (string.IsNullOrEmpty(reference) || reference.Length > MaxReferenceLength)
        {
            return false;
        }

        try
        {
            var number = ProtectorFor(productKey).Unprotect(reference);
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

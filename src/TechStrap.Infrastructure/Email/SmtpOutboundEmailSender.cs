using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using SyntaxCircus.Email;
using TechStrap.Application.Email;

namespace TechStrap.Infrastructure.Email;

/// <summary>Adapts <see cref="IEmailSender"/> to <see cref="IOutboundEmailSender"/>, reducing every failure to a sanitized category.</summary>
internal sealed class SmtpOutboundEmailSender(IEmailSender sender, ILogger<SmtpOutboundEmailSender> logger) : IOutboundEmailSender
{
    public async Task<Result> SendAsync(OutboundEmail email, CancellationToken cancellationToken)
    {
        string category;
        try
        {
            await sender.SendAsync(
                new EmailMessage(email.To, email.Subject, email.Html, IsBodyHtml: true, From: QuoteDisplayName(email.From), PlainTextBody: email.Text, ReplyTo: email.ReplyTo)
                {
                    MessageId = email.MessageId,
                },
                cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SmtpDeliveryException ex)
        {
            category = ex.Kind switch
            {
                SmtpFailureKind.Transient => EmailSendFailures.Transient,
                SmtpFailureKind.Permanent => EmailSendFailures.Permanent,
                SmtpFailureKind.Authentication => EmailSendFailures.Authentication,
                SmtpFailureKind.Timeout => EmailSendFailures.Timeout,
                _ => EmailSendFailures.Unknown,
            };
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            category = EmailSendFailures.Timeout;
        }
#pragma warning disable CA1031 // Any other failure is reduced to a category on purpose: the exception can carry the host and credentials.
        catch (Exception)
#pragma warning restore CA1031
        {
            category = EmailSendFailures.Unknown;
        }

        // Never log the exception or its message: SMTP exceptions can carry the host and credentials.
        logger.LogWarning("Email {MessageId} was not sent: {Category}.", email.MessageId, category);
        return Result.Failure(new ResultError(category, "The email could not be sent.", ResultErrorKind.Failure));
    }

    /// <summary>
    /// The renderer builds "{DisplayName} &lt;{address}&gt;". A display name with a comma, quote or other special would be
    /// mis-parsed, so the name is quoted as an RFC 5322 quoted-string (control characters dropped, backslash and quote escaped).
    /// </summary>
    internal static string? QuoteDisplayName(string? from)
    {
        if (string.IsNullOrWhiteSpace(from) || !from.EndsWith('>') || from.LastIndexOf('<') is not (> 0 and var open))
        {
            return from;
        }

        var name = new string(from[..open].Where(c => !char.IsControl(c)).ToArray()).Trim();
        var address = from[open..];
        if (name.Length == 0)
        {
            return address;
        }

        return $"\"{name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\" {address}";
    }
}

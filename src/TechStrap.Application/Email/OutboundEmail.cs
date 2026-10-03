using SyntaxCircus.Common;

namespace TechStrap.Application.Email;

public sealed record OutboundEmail(string To, string Subject, string Text, string Html, string? From, string? ReplyTo, string MessageId);

/// <summary>Sends one rendered email (D-033). Failures are sanitised categories from <see cref="EmailSendFailures"/>, never server text.</summary>
public interface IOutboundEmailSender
{
    Task<Result> SendAsync(OutboundEmail email, CancellationToken cancellationToken);
}

public static class EmailSendFailures
{
    public const string Transient = "smtp-transient";
    public const string Permanent = "smtp-permanent";
    public const string Authentication = "smtp-authentication";
    public const string Timeout = "smtp-timeout";
    public const string Unknown = "smtp-unknown";
}

/// <summary>The outbox id goes in Message-ID so duplicate sends are recognisable (D-010).</summary>
public static class OutboundMessageIds
{
    public const string Domain = "techstrap.local";

    public static string For(Guid outboxId) => $"{outboxId:D}@{Domain}";
}

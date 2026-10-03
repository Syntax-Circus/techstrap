using Microsoft.Extensions.Logging;
using MimeKit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Email;
using TechStrap.Application.Email;
using TechStrap.Infrastructure.Email;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class SmtpOutboundEmailSenderTests
{
    private static readonly OutboundEmail Email = new("ann@example.com", "subj", "text", "<p>html</p>", "Orbitly <help@orbitly.test>", "reply@orbitly.test", "id-1@techstrap.local");

    private readonly IEmailSender _inner = Substitute.For<IEmailSender>();
    private readonly ListLogger _logger = new();
    private readonly SmtpOutboundEmailSender _sender;

    public SmtpOutboundEmailSenderTests() => _sender = new SmtpOutboundEmailSender(_inner, _logger);

    [Fact]
    public async Task It_maps_the_email_onto_an_html_message_with_a_text_part_and_the_message_id()
    {
        EmailMessage? captured = null;
        await _inner.SendAsync(Arg.Do<EmailMessage>(m => captured = m), Arg.Any<CancellationToken>());

        var result = await _sender.SendAsync(Email, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured.To.ShouldBe("ann@example.com");
        captured.Subject.ShouldBe("subj");
        captured.Body.ShouldBe("<p>html</p>");
        captured.IsBodyHtml.ShouldBeTrue();
        captured.PlainTextBody.ShouldBe("text");
        var from = MailboxAddress.Parse(captured.From!);
        from.Address.ShouldBe("help@orbitly.test");
        from.Name.ShouldBe("Orbitly");
        captured.ReplyTo.ShouldBe("reply@orbitly.test");
        captured.MessageId.ShouldBe("id-1@techstrap.local");
    }

    [Fact]
    public async Task A_display_name_with_specials_keeps_its_name_and_address()
    {
        EmailMessage? captured = null;
        await _inner.SendAsync(Arg.Do<EmailMessage>(m => captured = m), Arg.Any<CancellationToken>());

        await _sender.SendAsync(Email with { From = "Orbitly, \"Support\" Inc. <help@orbitly.test>" }, CancellationToken.None);

        var parsed = MailboxAddress.Parse(captured!.From!);
        parsed.Address.ShouldBe("help@orbitly.test");
        parsed.Name.ShouldBe("Orbitly, \"Support\" Inc.");
    }

    [Fact]
    public async Task A_missing_from_stays_missing()
    {
        EmailMessage? captured = null;
        await _inner.SendAsync(Arg.Do<EmailMessage>(m => captured = m), Arg.Any<CancellationToken>());

        await _sender.SendAsync(Email with { From = null }, CancellationToken.None);

        captured!.From.ShouldBeNull();
    }

    [Fact]
    public async Task Any_exception_becomes_a_category_and_its_text_is_never_logged()
    {
        _inner.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("auth failed for smtp-user@mail.secret-host.test:587"));

        var result = await _sender.SendAsync(Email, CancellationToken.None);

        result.Errors[0].Code.ShouldBe(EmailSendFailures.Unknown);
        _logger.Lines.ShouldNotBeEmpty();
        _logger.Lines.ShouldAllBe(line => !line.Contains("secret-host") && !line.Contains("smtp-user"));
    }

    [Fact]
    public async Task A_timeout_is_a_timeout_category()
    {
        _inner.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException());

        var result = await _sender.SendAsync(Email, CancellationToken.None);

        result.Errors[0].Code.ShouldBe(EmailSendFailures.Timeout);
    }

    [Fact]
    public async Task Caller_cancellation_is_rethrown_not_categorised()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        _inner.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException(source.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => _sender.SendAsync(Email, source.Token));
    }

    private sealed class ListLogger : ILogger<SmtpOutboundEmailSender>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
            if (exception is not null)
            {
                Lines.Add(exception.ToString());
            }
        }
    }
}

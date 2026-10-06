using System.Text.Json;
using Serilog;
using Serilog.Events;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>The capability to call the API as a ticket's customer: a value that cannot be printed by accident and cannot carry a character a header or a path could misread.</summary>
public sealed class TicketTokenTests
{
    private static readonly string Valid = "AbC-_0123456789AbC-_0123456789AbC-_0123456789"[..TicketToken.Length];

    [Fact]
    public void A_43_character_base64url_value_is_a_token_and_exposes_its_value()
    {
        TicketToken.TryParse(Valid, out var token).ShouldBeTrue();

        token.Value.ShouldBe(Valid);
        Valid.Length.ShouldBe(43);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void Nothing_or_something_short_is_not_a_token(string? text)
    {
        TicketToken.TryParse(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void One_character_too_few_or_too_many_is_not_a_token()
    {
        TicketToken.TryParse(Valid[..42], out _).ShouldBeFalse();
        TicketToken.TryParse(Valid + "A", out _).ShouldBeFalse();
    }

    // The last character of an otherwise valid value: each one could split a header, end a path segment, add a query or be decoded by something downstream.
    [Theory]
    [InlineData(' ')]
    [InlineData('\r')]
    [InlineData('\n')]
    [InlineData('\t')]
    [InlineData('/')]
    [InlineData('\\')]
    [InlineData('%')]
    [InlineData('+')]
    [InlineData('=')]
    [InlineData('.')]
    [InlineData('?')]
    [InlineData('#')]
    [InlineData(';')]
    [InlineData((char)0xE9)]
    public void A_value_with_a_character_outside_base64url_is_not_a_token(char last)
    {
        TicketToken.TryParse(Valid[..42] + last, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_trailing_newline_after_43_good_characters_is_not_a_token()
    {
        TicketToken.TryParse(Valid + "\n", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_token_never_prints_its_value()
    {
        TicketToken.TryParse(Valid, out var token).ShouldBeTrue();

        token.ToString().ShouldBe("[token]");
        $"{token}".ShouldNotContain(Valid);
        string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", token).ShouldNotContain(Valid);
    }

    [Fact]
    public void The_default_token_has_no_value_and_cannot_be_used()
    {
        var token = default(TicketToken);

        Should.Throw<InvalidOperationException>(() => token.Value);
        token.ToString().ShouldBe("[token]");
    }

    [Fact]
    public void A_serializer_or_a_log_destructurer_cannot_read_the_value()
    {
        TicketToken.TryParse(Valid, out var token).ShouldBeTrue();
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();

        logger.Verbose("Token {@Token} / {Token}", token, token);

        JsonSerializer.Serialize(token).ShouldNotContain(Valid);
        var evt = sink.Events.ShouldHaveSingleItem();
        string.Join('|', evt.RenderMessage(), string.Join('|', evt.Properties.Values.Select(v => v.ToString()))).ShouldNotContain(Valid);
    }
}

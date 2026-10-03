using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Tickets;

public sealed class TicketNumberTests
{
    [Fact]
    public void A_number_formats_as_prefix_dash_sequence()
    {
        TicketNumber.Create("ACME", 142).Value.ToString().ShouldBe("ACME-142");
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("A")]
    [InlineData("1ACME")]
    [InlineData("ACME-X")]
    [InlineData("ABCDEFGHIJK")]
    [InlineData("")]
    public void An_invalid_prefix_is_a_validation_error(string prefix)
    {
        var result = TicketNumber.Create(prefix, 1);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Kind.ShouldBe(DomainErrorKind.Validation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_sequence_below_one_is_a_validation_error(long sequence)
    {
        TicketNumber.Create("ACME", sequence).Error!.Code.ShouldBe("ticket-number-sequence-invalid");
    }

    [Theory]
    [InlineData("ACME-142", "ACME", 142)]
    [InlineData("  acme-7 ", "ACME", 7)]
    [InlineData("A1B2-100000", "A1B2", 100000)]
    public void TryParse_reads_the_documented_format_case_insensitively(string text, string prefix, long sequence)
    {
        TicketNumber.TryParse(text, out var number).ShouldBeTrue();

        number.Prefix.ShouldBe(prefix);
        number.Sequence.ShouldBe(sequence);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ACME")]
    [InlineData("-142")]
    [InlineData("ACME-0")]
    [InlineData("ACME-abc")]
    [InlineData(null)]
    public void TryParse_rejects_malformed_numbers(string? text)
    {
        TicketNumber.TryParse(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void Two_numbers_with_the_same_prefix_and_sequence_are_equal()
    {
        TicketNumber.Create("ACME", 5).Value.ShouldBe(TicketNumber.Create("ACME", 5).Value);
    }

    [Theory]
    [InlineData("ACME\n")]
    [InlineData("ACME\r\n")]
    [InlineData(" ACME")]
    public void A_prefix_with_trailing_or_leading_whitespace_is_invalid(string prefix)
    {
        TicketNumber.IsValidPrefix(prefix).ShouldBeFalse();
        TicketNumber.Create(prefix, 1).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("ACME\n-5")]
    [InlineData("ACME-+5")]
    [InlineData("ACME- 5")]
    [InlineData("ACME-5\n5")]
    [InlineData("ACME-1,000")]
    [InlineData("ACME-1e3")]
    public void TryParse_rejects_signs_whitespace_and_separators_inside_the_number(string text)
    {
        TicketNumber.TryParse(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void The_default_number_formats_as_an_empty_string_not_a_dash_and_zero()
    {
        default(TicketNumber).ToString().ShouldBeEmpty();
    }

    [Fact]
    public void The_longest_number_fits_the_ticket_number_column_limit()
    {
        var longest = TicketNumber.Create(new string('A', DomainLimits.NumberPrefixMaxLength), 9_999_999_999_999L).Value;

        longest.ToString().Length.ShouldBeLessThanOrEqualTo(DomainLimits.TicketNumberMaxLength);
        DomainLimits.TicketNumberMaxLength.ShouldBe(24);
    }
}

using TechStrap.Contracts.DeadLetters;

namespace TechStrap.Architecture.Tests;

public sealed class DeadLetterDtoShapeTests
{
    private static readonly string[] _forbidden = ["ToAddress", "Address", "Email", "Payload", "PayloadJson", "Link", "Token"];

    [Fact]
    public void The_dead_letter_dto_exposes_no_address_payload_or_link_fields()
    {
        var names = typeof(DeadLetterDto).GetProperties().Select(p => p.Name).ToList();

        names.ShouldNotBeEmpty();
        names.ShouldNotContain(name => _forbidden.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }
}

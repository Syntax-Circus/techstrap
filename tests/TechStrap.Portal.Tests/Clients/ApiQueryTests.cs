using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>A query string built for the API: every value is escaped, so the visitor's text can never add a parameter, end the query or start a fragment.</summary>
public sealed class ApiQueryTests
{
    [Fact]
    public void A_path_with_no_pairs_is_unchanged()
    {
        ApiQuery.Build("api/public/kb/x/search").ShouldBe("api/public/kb/x/search");
    }

    [Fact]
    public void Pairs_are_joined_in_order_and_a_null_value_is_left_out()
    {
        ApiQuery.Build("api/x", ("q", "reset"), ("category", null), ("pageSize", "5")).ShouldBe("api/x?q=reset&pageSize=5");
    }

    [Fact]
    public void Every_value_is_escaped_so_it_cannot_add_a_parameter_or_a_fragment()
    {
        ApiQuery.Build("api/x", ("q", "a&b=c d#e?f+g")).ShouldBe("api/x?q=a%26b%3Dc%20d%23e%3Ff%2Bg");
    }

    [Fact]
    public void A_name_is_escaped_too()
    {
        ApiQuery.Build("api/x", ("a&b", "1")).ShouldBe("api/x?a%26b=1");
    }

    [Fact]
    public void Only_null_values_leave_the_bare_path()
    {
        ApiQuery.Build("api/x", ("q", null)).ShouldBe("api/x");
    }

    [Fact]
    public void Non_ascii_text_is_escaped_as_utf8()
    {
        ApiQuery.Build("api/x", ("q", "caf" + (char)0xE9)).ShouldBe("api/x?q=caf%C3%A9");
    }
}

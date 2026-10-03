namespace TechStrap.Domain.Tests;

public sealed class DomainResultTests
{
    [Fact]
    public void Ok_has_no_error()
    {
        var result = DomainResult.Ok();

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void A_failed_generic_result_carries_the_error_and_refuses_to_give_a_value()
    {
        DomainResult<int> result = DomainErrors.Conflict("c", "m");

        result.IsFailure.ShouldBeTrue();
        result.Error!.Kind.ShouldBe(DomainErrorKind.Conflict);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void A_successful_generic_result_carries_the_value()
    {
        DomainResult<int>.Ok(7).Value.ShouldBe(7);
    }
}

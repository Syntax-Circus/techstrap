using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Domain;

namespace TechStrap.Application.Tests.Results;

public sealed class DomainResultExtensionsTests
{
    [Fact]
    public void A_successful_domain_result_becomes_a_successful_result()
    {
        DomainResult.Ok().ToResult().IsSuccess.ShouldBeTrue();
        DomainResult<int>.Ok(5).ToResult().Value.ShouldBe(5);
    }

    [Theory]
    [InlineData(DomainErrorKind.Validation, ResultErrorKind.Validation)]
    [InlineData(DomainErrorKind.NotFound, ResultErrorKind.NotFound)]
    [InlineData(DomainErrorKind.Conflict, ResultErrorKind.Conflict)]
    public void A_failed_domain_result_keeps_code_and_message_maps_the_kind_and_keeps_a_target_only_for_validation(DomainErrorKind domainKind, ResultErrorKind expected)
    {
        var failure = DomainResult.Fail(new DomainError(domainKind, "the-code", "The message.", "field"));

        var result = failure.ToResult();

        result.IsSuccess.ShouldBeFalse();
        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("the-code");
        error.Message.ShouldBe("The message.");
        error.Kind.ShouldBe(expected);
        error.Target.ShouldBe(domainKind == DomainErrorKind.Validation ? "field" : null);
    }

    [Fact]
    public void A_failed_generic_domain_result_becomes_a_failed_generic_result()
    {
        DomainResult<string> failure = DomainErrors.Conflict("c", "m");

        var result = failure.ToResult();

        result.IsSuccess.ShouldBeFalse();
        var error = result.Errors.ShouldHaveSingleItem();
        error.Kind.ShouldBe(ResultErrorKind.Conflict);
        error.Code.ShouldBe("c");
        error.Message.ShouldBe("m");
        error.Target.ShouldBeNull();
    }

    [Fact]
    public void Every_domain_error_kind_maps_without_throwing()
    {
        foreach (var kind in Enum.GetValues<DomainErrorKind>())
        {
            Should.NotThrow(() => new DomainError(kind, "code", "message").ToError());
        }
    }

    [Fact]
    public void The_outbox_dead_lettered_code_is_the_one_the_domain_returns()
    {
        PersistenceErrorCodes.OutboxDeadLettered.ShouldBe("outbox-dead-lettered");
    }
}

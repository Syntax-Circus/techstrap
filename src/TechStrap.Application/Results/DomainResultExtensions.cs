using SyntaxCircus.Common;
using TechStrap.Domain;

namespace TechStrap.Application.Results;

/// <summary>Converts the BCL-only <see cref="DomainResult"/> into the transport-neutral <see cref="Result"/> (D-028).</summary>
public static class DomainResultExtensions
{
    public static Result ToResult(this DomainResult result) =>
        result.IsSuccess ? Result.Success() : Result.Failure(ToError(result.Error!));

    public static Result<T> ToResult<T>(this DomainResult<T> result) =>
        result.IsSuccess ? Result<T>.Success(result.Value) : Result<T>.Failure(ToError(result.Error!));

    /// <summary>The result type allows a target only on validation errors, so other kinds drop it.</summary>
    public static ResultError ToError(this DomainError error) =>
        new(error.Code, error.Message, ToKind(error.Kind), error.Kind == DomainErrorKind.Validation ? error.Target : null);

    private static ResultErrorKind ToKind(DomainErrorKind kind) => kind switch
    {
        DomainErrorKind.Validation => ResultErrorKind.Validation,
        DomainErrorKind.NotFound => ResultErrorKind.NotFound,
        DomainErrorKind.Conflict => ResultErrorKind.Conflict,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped domain error kind."),
    };
}

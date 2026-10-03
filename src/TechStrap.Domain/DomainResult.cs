namespace TechStrap.Domain;

/// <summary>The kind of a domain failure. Application maps these onto the transport-neutral result kinds.</summary>
public enum DomainErrorKind
{
    Validation,
    NotFound,
    Conflict,
}

/// <summary>A domain failure: a stable code, a client-safe message and an optional field the failure is about.</summary>
public sealed record DomainError(DomainErrorKind Kind, string Code, string Message, string? Target = null);

/// <summary>Factory helpers so domain code reads <c>return DomainErrors.Conflict("code", "message");</c>.</summary>
public static class DomainErrors
{
    public static DomainError Validation(string code, string message, string? target = null) =>
        new(DomainErrorKind.Validation, code, message, target);

    public static DomainError NotFound(string code, string message) =>
        new(DomainErrorKind.NotFound, code, message);

    public static DomainError Conflict(string code, string message) =>
        new(DomainErrorKind.Conflict, code, message);
}

/// <summary>
/// Outcome of a domain operation. Domain is BCL-only (no packages), so it cannot use the SyntaxCircus.Common result type;
/// Application converts with <c>ToResult()</c> (D-028).
/// </summary>
public class DomainResult
{
    private static readonly DomainResult Succeeded = new(null);

    protected DomainResult(DomainError? error) => Error = error;

    public DomainError? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => Error is not null;

    public static DomainResult Ok() => Succeeded;

    public static DomainResult Fail(DomainError error) => new(error);

    public static implicit operator DomainResult(DomainError error) => Fail(error);
}

/// <summary>A domain outcome that carries a value on success.</summary>
public sealed class DomainResult<T> : DomainResult
{
    private readonly T? _value;

    private DomainResult(T? value, DomainError? error)
        : base(error) => _value = value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no value.");

    public static DomainResult<T> Ok(T value) => new(value, null);

    public static new DomainResult<T> Fail(DomainError error) => new(default, error);

    public static implicit operator DomainResult<T>(DomainError error) => Fail(error);
}

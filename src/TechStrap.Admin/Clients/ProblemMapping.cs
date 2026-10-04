using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;

namespace TechStrap.Admin.Clients;

/// <summary>
/// Turns a non-success API response into <see cref="Result"/> errors. The API answers with RFC 7807 problem details whose <c>type</c> is the error code;
/// a validation failure (400) carries <c>type</c> "validation-failed" and the specific codes in the <c>errorCodes</c> extension, keyed by field.
/// </summary>
internal static class ProblemMapping
{
    public static IReadOnlyList<ResultError> Map(HttpStatusCode status, string? body)
    {
        var problem = Problem.TryParse(body);
        var kind = KindOf(status);
        var message = problem.Detail ?? problem.Title ?? DefaultMessage(status);

        if (kind == ResultErrorKind.Validation)
        {
            var errors = ValidationErrors(problem, message);
            if (errors.Count > 0)
            {
                return errors;
            }
        }

        var code = !string.IsNullOrWhiteSpace(problem.Type) ? problem.Type : DefaultCode(status);
        return [new ResultError(code, message, kind)];
    }

    public static ResultErrorKind KindOf(HttpStatusCode status) => (int)status switch
    {
        400 => ResultErrorKind.Validation,
        401 => ResultErrorKind.Unauthenticated,
        403 => ResultErrorKind.Forbidden,
        404 => ResultErrorKind.NotFound,
        409 => ResultErrorKind.Conflict,
        _ => ResultErrorKind.Failure,
    };

    private static List<ResultError> ValidationErrors(Problem problem, string fallbackMessage)
    {
        var errors = new List<ResultError>();
        foreach (var (field, codes) in problem.ErrorCodes)
        {
            for (var i = 0; i < codes.Length; i++)
            {
                var messages = problem.Errors.GetValueOrDefault(field);
                var message = messages is not null && i < messages.Length ? messages[i] : fallbackMessage;
                errors.Add(new ResultError(codes[i], message, ResultErrorKind.Validation, field.Length == 0 ? null : field));
            }
        }

        return errors;
    }

    private static string DefaultCode(HttpStatusCode status) => (int)status switch
    {
        400 => ApiErrorCodes.ValidationFailed,
        401 => ApiErrorCodes.Unauthenticated,
        403 => ApiErrorCodes.Forbidden,
        404 => ApiErrorCodes.NotFound,
        409 => ApiErrorCodes.Conflict,
        502 or 503 or 504 => ApiErrorCodes.ApiUnavailable,
        _ => ApiErrorCodes.ApiError,
    };

    private static string DefaultMessage(HttpStatusCode status) => (int)status switch
    {
        401 => "Your session has expired. Sign in again.",
        403 => "You do not have access to this.",
        404 => "That item no longer exists.",
        502 or 503 or 504 => "TechStrap could not reach the API. Try again in a moment.",
        _ => "The request failed. Try again, and tell an admin if it keeps happening.",
    };

    private sealed record Problem(
        string? Type,
        string? Title,
        string? Detail,
        IReadOnlyDictionary<string, string[]> Errors,
        IReadOnlyDictionary<string, string[]> ErrorCodes)
    {
        private static readonly Problem Empty = new(null, null, null, new Dictionary<string, string[]>(), new Dictionary<string, string[]>());

        public static Problem TryParse(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return Empty;
                }

                return new Problem(
                    Text(root, "type"),
                    Text(root, "title"),
                    Text(root, "detail"),
                    Dictionary(root, "errors"),
                    Dictionary(root, "errorCodes"));
            }
            catch (JsonException)
            {
                return Empty;
            }
        }

        private static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

        private static Dictionary<string, string[]> Dictionary(JsonElement root, string name)
        {
            var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array))
                {
                    result[property.Name] = [.. property.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)];
                }
            }

            return result;
        }
    }
}

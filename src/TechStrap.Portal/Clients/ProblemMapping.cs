using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;

namespace TechStrap.Portal.Clients;

/// <summary>
/// Turns a non-success API response into <see cref="Result"/> errors (D-045). The API answers with RFC 7807 problem details; a validation failure (400) carries the specific codes in the
/// <c>errorCodes</c> extension, keyed by field, and the messages in <c>errors</c>. Every other status is mapped by the status alone, to a fixed code and a fixed sentence (<see cref="ProblemCopy"/>):
/// 404 is one not-found whatever the API called it (so nothing can tell an unknown product from an unknown ticket), 413 and 415 are the attachment errors, 429 is rate limited, and any 5xx is
/// "unavailable" (a write may or may not have been applied, and the API's own text is never shown).
/// </summary>
internal static class ProblemMapping
{
    public static IReadOnlyList<ResultError> Map(HttpStatusCode status, string? body)
    {
        switch ((int)status)
        {
            case 400:
                var problem = Problem.TryParse(body);
                var errors = ValidationErrors(problem);
                return errors.Count > 0
                    ? errors
                    : [new ResultError(ApiErrorCodes.ValidationFailed, problem.Detail ?? ProblemCopy.Invalid, ResultErrorKind.Validation)];
            case 404:
                return [NotFound()];
            case 413:
                return [new ResultError(ApiErrorCodes.PayloadTooLarge, ProblemCopy.PayloadTooLarge, ResultErrorKind.Failure)];
            case 415:
                return [new ResultError(ApiErrorCodes.UnsupportedMediaType, ProblemCopy.UnsupportedMediaType, ResultErrorKind.Failure)];
            case 429:
                return [new ResultError(ApiErrorCodes.RateLimited, ProblemCopy.RateLimited, ResultErrorKind.Failure)];
            case >= 500:
                return [Unavailable()];
            default:
                return [new ResultError(ApiErrorCodes.ApiError, ProblemCopy.ApiError, ResultErrorKind.Failure)];
        }
    }

    /// <summary>The one not-found: also what a client answers for a key or token that is malformed, without calling the API.</summary>
    public static ResultError NotFound() => new(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound);

    public static ResultError Unavailable() => new(ApiErrorCodes.ApiUnavailable, ProblemCopy.ApiUnavailable, ResultErrorKind.Failure);

    public static ResultError Unexpected() => new(ApiErrorCodes.UnexpectedResponse, ProblemCopy.UnexpectedResponse, ResultErrorKind.Failure);

    private static List<ResultError> ValidationErrors(Problem problem)
    {
        var errors = new List<ResultError>();
        foreach (var (field, codes) in problem.ErrorCodes)
        {
            for (var i = 0; i < codes.Length; i++)
            {
                var messages = problem.Errors.GetValueOrDefault(field);
                var message = messages is not null && i < messages.Length ? messages[i] : problem.Detail ?? ProblemCopy.Invalid;
                errors.Add(new ResultError(codes[i], message, ResultErrorKind.Validation, field.Length == 0 ? null : field));
            }
        }

        return errors;
    }

    private sealed record Problem(string? Detail, IReadOnlyDictionary<string, string[]> Errors, IReadOnlyDictionary<string, string[]> ErrorCodes)
    {
        private static readonly Problem Empty = new(null, new Dictionary<string, string[]>(), new Dictionary<string, string[]>());

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
                return root.ValueKind == JsonValueKind.Object ? new Problem(Text(root, "detail"), Dictionary(root, "errors"), Dictionary(root, "errorCodes")) : Empty;
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

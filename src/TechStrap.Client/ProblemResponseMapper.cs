using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;

namespace TechStrap.Client;

/// <summary>
/// Turns a non-success API response into <see cref="Result"/> errors. The API answers with RFC 7807 problem details; a validation failure (400, 422) carries the specific codes in the <c>errorCodes</c>
/// extension, keyed by field, and the messages in <c>errors</c>. Every other status maps by the status alone to a fixed code and a fixed sentence (<see cref="TechStrapClientMessages"/>): the server's own
/// text is never shown for any status except a validation failure, whose <c>detail</c> and per-field messages are written for the user ("Add a subject."), never a token, a host or exception text.
/// </summary>
internal static class ProblemResponseMapper
{
    public static IReadOnlyList<ResultError> Map(HttpStatusCode status, string? body)
    {
        switch ((int)status)
        {
            case 400 or 422:
                var problem = Problem.TryParse(body);
                var errors = ValidationErrors(problem);
                return errors.Count > 0
                    ? errors
                    : [new ResultError(TechStrapClientErrorCodes.ValidationFailed, problem.Detail ?? TechStrapClientMessages.Invalid, ResultErrorKind.Validation)];
            case 401:
                return [new ResultError(TechStrapClientErrorCodes.InvalidApiKey, TechStrapClientMessages.InvalidApiKey, ResultErrorKind.Unauthenticated)];
            case 403:
                return [new ResultError(TechStrapClientErrorCodes.InvalidApiKey, TechStrapClientMessages.InvalidApiKey, ResultErrorKind.Forbidden)];
            case 413:
                return [new ResultError(TechStrapClientErrorCodes.PayloadTooLarge, TechStrapClientMessages.PayloadTooLarge, ResultErrorKind.Failure)];
            case 415:
                return [new ResultError(TechStrapClientErrorCodes.UnsupportedMediaType, TechStrapClientMessages.UnsupportedMediaType, ResultErrorKind.Failure)];
            case 429:
                return [new ResultError(TechStrapClientErrorCodes.RateLimited, TechStrapClientMessages.RateLimited, ResultErrorKind.Failure)];
            case >= 500:
                return [Unavailable()];
            default:
                return [new ResultError(TechStrapClientErrorCodes.ApiError, TechStrapClientMessages.ApiError, ResultErrorKind.Failure)];
        }
    }

    /// <summary>Also what the client reports for an unreachable API, a timeout or an open circuit: fixed copy, never exception text or a host.</summary>
    public static ResultError Unavailable() => new(TechStrapClientErrorCodes.ApiUnavailable, TechStrapClientMessages.ApiUnavailable, ResultErrorKind.Failure);

    public static ResultError Unexpected() => new(TechStrapClientErrorCodes.UnexpectedResponse, TechStrapClientMessages.UnexpectedResponse, ResultErrorKind.Failure);

    private static List<ResultError> ValidationErrors(Problem problem)
    {
        var errors = new List<ResultError>();
        foreach (var (field, codes) in problem.ErrorCodes)
        {
            for (var i = 0; i < codes.Length; i++)
            {
                var messages = problem.Errors.GetValueOrDefault(field);
                var message = messages is not null && i < messages.Length ? messages[i] : problem.Detail ?? TechStrapClientMessages.Invalid;
                errors.Add(new ResultError(codes[i], message, ResultErrorKind.Validation, field.Length == 0 ? null : field));
            }
        }

        return errors;
    }

    private sealed record Problem(string? Detail, IReadOnlyDictionary<string, string[]> Errors, IReadOnlyDictionary<string, string[]> ErrorCodes)
    {
        private static readonly Problem _empty = new(null, new Dictionary<string, string[]>(), new Dictionary<string, string[]>());

        public static Problem TryParse(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return _empty;
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                return root.ValueKind == JsonValueKind.Object ? new Problem(Text(root, "detail"), Dictionary(root, "errors"), Dictionary(root, "errorCodes")) : _empty;
            }
            catch (JsonException)
            {
                return _empty;
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

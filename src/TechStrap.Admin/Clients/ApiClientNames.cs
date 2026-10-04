namespace TechStrap.Admin.Clients;

/// <summary>The two named HTTP clients every API call goes through (D-040). Both run the auth handler; only the read client retries.</summary>
public static class ApiClientNames
{
    /// <summary>Idempotent GETs: retried up to <see cref="ApiClientRegistration.ReadRetryCount"/> times on transport errors, timeouts and 408/429/5xx.</summary>
    public const string Read = "techstrap-api-read";

    /// <summary>Every POST, PUT and DELETE: no retry and no circuit breaker, so a transient failure can never duplicate a reply or a note.</summary>
    public const string Write = "techstrap-api-write";
}

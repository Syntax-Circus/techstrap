namespace TechStrap.Portal.Clients;

/// <summary>The two named HTTP clients every API call goes through (D-045). Both forward the visitor's address; only the read client retries.</summary>
public static class ApiClientNames
{
    /// <summary>Idempotent GETs: retried up to <see cref="ApiClientRegistration.ReadRetryCount"/> times on transport errors, timeouts and 408/502/503/504 (never on 500, no circuit breaker).</summary>
    public const string Read = "techstrap-portal-api-read";

    /// <summary>Every POST, PUT and DELETE: no retry and no circuit breaker, so a transient failure can never duplicate a ticket or a reply.</summary>
    public const string Write = "techstrap-portal-api-write";
}

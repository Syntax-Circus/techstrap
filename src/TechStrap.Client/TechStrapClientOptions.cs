namespace TechStrap.Client;

/// <summary>
/// Settings for the TechStrap client. Validated the first time the options are read, not at start (a MAUI app has no generic host). The key is a secret: it is never written to a message,
/// a log line or <see cref="ToString"/>.
/// </summary>
public sealed class TechStrapClientOptions
{
    /// <summary>Configuration key for <see cref="BaseAddress"/>.</summary>
    public const string BaseAddressKey = "BaseAddress";

    /// <summary>Configuration key for <see cref="ApiKey"/>.</summary>
    public const string ApiKeyKey = "ApiKey";

    /// <summary>Configuration key for <see cref="Timeout"/>.</summary>
    public const string TimeoutKey = "Timeout";

    /// <summary>Configuration key for <see cref="MaxAttempts"/>.</summary>
    public const string MaxAttemptsKey = "MaxAttempts";

    /// <summary>Configuration key for <see cref="RetryBaseDelay"/>.</summary>
    public const string RetryBaseDelayKey = "RetryBaseDelay";

    /// <summary>Configuration key for <see cref="MaxRetryDelay"/>.</summary>
    public const string MaxRetryDelayKey = "MaxRetryDelay";

    /// <summary>The TechStrap API address: absolute http or https, no user info, query or fragment. https unless the host is loopback.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>The API key sent in the key header on every request. Never embed a Trusted key in an app that ships to customers.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The total time budget for one call, retries included. Greater than zero, at most ten minutes.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The most times one call is sent, the first try included. From 1 to 10.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>The wait before the first retry; later waits grow from it. Greater than zero and not above <see cref="MaxRetryDelay"/>.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The longest wait between two tries.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The type name and the address only, never the key.</summary>
    public override string ToString() => $"{nameof(TechStrapClientOptions)} {{ {nameof(BaseAddress)} = {BaseAddress} }}";
}

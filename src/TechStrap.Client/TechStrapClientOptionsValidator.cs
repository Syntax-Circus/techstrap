using System.Globalization;
using Microsoft.Extensions.Options;

namespace TechStrap.Client;

/// <summary>Checks <see cref="TechStrapClientOptions"/> on first use. Each message names the setting; none of them carries the key.</summary>
internal sealed class TechStrapClientOptionsValidator : IValidateOptions<TechStrapClientOptions>
{
    private const int MaxAttemptsLimit = 10;
    private static readonly TimeSpan _maxTimeout = TimeSpan.FromMinutes(10);

    public ValidateOptionsResult Validate(string? name, TechStrapClientOptions options)
    {
        var failures = new List<string>();

        ValidateBaseAddress(options.BaseAddress, failures);
        ValidateApiKey(options.ApiKey, failures);

        if (options.Timeout <= TimeSpan.Zero || options.Timeout > _maxTimeout)
        {
            failures.Add($"{nameof(TechStrapClientOptions.Timeout)} must be greater than zero and at most 10 minutes.");
        }

        if (options.MaxAttempts < 1 || options.MaxAttempts > MaxAttemptsLimit)
        {
            failures.Add($"{nameof(TechStrapClientOptions.MaxAttempts)} must be from 1 to {MaxAttemptsLimit.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (options.RetryBaseDelay <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(TechStrapClientOptions.RetryBaseDelay)} must be greater than zero.");
        }
        else if (options.RetryBaseDelay > options.MaxRetryDelay)
        {
            failures.Add($"{nameof(TechStrapClientOptions.RetryBaseDelay)} must not be greater than {nameof(TechStrapClientOptions.MaxRetryDelay)}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateBaseAddress(Uri? address, List<string> failures)
    {
        const string Setting = nameof(TechStrapClientOptions.BaseAddress);

        if (address is null)
        {
            failures.Add($"{Setting} is required: the absolute address of the TechStrap API, for example https://support.example.com/.");
            return;
        }

        if (!address.IsAbsoluteUri || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{Setting} must be an absolute http or https URL.");
            return;
        }

        if (!string.IsNullOrEmpty(address.UserInfo) || address.Query.Length != 0 || address.Fragment.Length != 0)
        {
            failures.Add($"{Setting} must not contain user info, a query or a fragment.");
        }

        if (address.Scheme == Uri.UriSchemeHttp && !address.IsLoopback)
        {
            failures.Add($"{Setting} must use https unless the host is loopback (localhost, 127.0.0.1 or [::1]).");
        }
    }

    private static void ValidateApiKey(string? key, List<string> failures)
    {
        const string Setting = nameof(TechStrapClientOptions.ApiKey);

        if (string.IsNullOrWhiteSpace(key))
        {
            failures.Add($"{Setting} is required.");
        }
        else if (!IsHeaderSafe(key))
        {
            failures.Add($"{Setting} must be printable ASCII with no spaces or control characters, so it can travel in a header.");
        }
    }

    private static bool IsHeaderSafe(string value) => value.All(c => c is > ' ' and < '\u007f');
}

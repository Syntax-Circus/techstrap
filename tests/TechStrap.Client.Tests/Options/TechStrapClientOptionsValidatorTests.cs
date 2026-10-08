using Microsoft.Extensions.Options;

namespace TechStrap.Client.Tests.Options;

public sealed class TechStrapClientOptionsValidatorTests
{
    private const string Key = "sk_live_0123456789abcdef";

    private static TechStrapClientOptions Valid() => new() { BaseAddress = new Uri("https://support.example.com/"), ApiKey = Key };

    private static ValidateOptionsResult Validate(TechStrapClientOptions options) => new TechStrapClientOptionsValidator().Validate(null, options);

    private static string Message(TechStrapClientOptions options) => Message(Validate(options));

    private static string Message(ValidateOptionsResult result) => result.FailureMessage ?? string.Empty;

    [Fact]
    public void Valid_options_pass_with_the_defaults()
    {
        var options = Valid();

        Validate(options).Succeeded.ShouldBeTrue();
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.MaxAttempts.ShouldBe(3);
        options.RetryBaseDelay.ShouldBe(TimeSpan.FromMilliseconds(500));
        options.MaxRetryDelay.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_missing_base_address_is_rejected()
    {
        var options = Valid();
        options.BaseAddress = null;

        Message(options).ShouldContain("BaseAddress");
    }

    [Theory]
    [InlineData("ftp://support.example.com/")]
    [InlineData("file:///c:/tmp/")]
    [InlineData("https://user:pass@support.example.com/")]
    [InlineData("https://user@support.example.com/")]
    [InlineData("https://support.example.com/?a=b")]
    [InlineData("https://support.example.com/#frag")]
    [InlineData("http://support.example.com/")]
    [InlineData("http://10.0.0.5/")]
    public void A_bad_base_address_is_rejected(string address)
    {
        var options = Valid();
        options.BaseAddress = new Uri(address);

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        Message(result).ShouldContain("BaseAddress");
    }

    [Fact]
    public void A_relative_base_address_is_rejected()
    {
        var options = Valid();
        options.BaseAddress = new Uri("/api/", UriKind.Relative);

        Message(options).ShouldContain("absolute");
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://localhost:5080/")]
    [InlineData("http://127.0.0.1:5080/")]
    [InlineData("http://[::1]:5080/")]
    [InlineData("https://support.example.com/api/")]
    public void Loopback_http_and_any_https_address_are_allowed(string address)
    {
        var options = Valid();
        options.BaseAddress = new Uri(address);

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("line\nbreak")]
    [InlineData("tab\there")]
    [InlineData("caf\u00e9")]
    [InlineData("del\u007f")]
    public void A_blank_or_header_unsafe_key_is_rejected(string? key)
    {
        var options = Valid();
        options.ApiKey = key;

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        Message(result).ShouldContain("ApiKey");
    }

    [Theory]
    [InlineData("0:00:00")]
    [InlineData("-0:00:01")]
    [InlineData("0:10:01")]
    public void A_timeout_outside_zero_to_ten_minutes_is_rejected(string timeout)
    {
        var options = Valid();
        options.Timeout = TimeSpan.Parse(timeout, System.Globalization.CultureInfo.InvariantCulture);

        Message(options).ShouldContain("Timeout");
    }

    [Theory]
    [InlineData("0:00:00.001")]
    [InlineData("0:10:00")]
    public void The_timeout_bounds_are_inclusive_of_the_upper_limit(string timeout)
    {
        var options = Valid();
        options.Timeout = TimeSpan.Parse(timeout, System.Globalization.CultureInfo.InvariantCulture);

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void Max_attempts_outside_one_to_ten_is_rejected(int attempts)
    {
        var options = Valid();
        options.MaxAttempts = attempts;

        Message(options).ShouldContain("MaxAttempts");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void Max_attempts_one_and_ten_are_allowed(int attempts)
    {
        var options = Valid();
        options.MaxAttempts = attempts;

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("0:00:00", "0:00:05")]
    [InlineData("-0:00:01", "0:00:05")]
    [InlineData("0:00:06", "0:00:05")]
    public void A_retry_base_delay_that_is_not_positive_or_exceeds_the_max_is_rejected(string baseDelay, string maxDelay)
    {
        var options = Valid();
        options.RetryBaseDelay = TimeSpan.Parse(baseDelay, System.Globalization.CultureInfo.InvariantCulture);
        options.MaxRetryDelay = TimeSpan.Parse(maxDelay, System.Globalization.CultureInfo.InvariantCulture);

        Message(options).ShouldContain("RetryBaseDelay");
    }

    [Fact]
    public void A_base_delay_equal_to_the_max_delay_is_allowed()
    {
        var options = Valid();
        options.RetryBaseDelay = TimeSpan.FromSeconds(5);
        options.MaxRetryDelay = TimeSpan.FromSeconds(5);

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Every_failure_is_reported_at_once()
    {
        var options = new TechStrapClientOptions { BaseAddress = null, ApiKey = " ", MaxAttempts = 0, Timeout = TimeSpan.Zero };

        Validate(options).Failures!.Count().ShouldBe(4);
    }

    [Fact]
    public void Validation_messages_never_contain_the_api_key()
    {
        // Everything that can be wrong, with a key that is itself wrong (header unsafe) so its rule fires too.
        const string Secret = "sk_secret value \u00e9";
        var options = new TechStrapClientOptions
        {
            BaseAddress = new Uri("http://user:pw@support.example.com/?q=1#f"),
            ApiKey = Secret,
            Timeout = TimeSpan.Zero,
            MaxAttempts = 99,
            RetryBaseDelay = TimeSpan.FromMinutes(1),
        };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.Failures!.Count().ShouldBeGreaterThanOrEqualTo(5);
        Message(result).ShouldNotContain(Secret);
        Message(result).ShouldNotContain("sk_secret");
    }

    [Fact]
    public void A_valid_key_never_appears_in_the_messages_of_other_failures()
    {
        var options = Valid();
        options.BaseAddress = null;
        options.MaxAttempts = 0;

        Message(options).ShouldNotContain(Key);
    }
}

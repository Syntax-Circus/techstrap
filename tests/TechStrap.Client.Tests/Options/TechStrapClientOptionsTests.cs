namespace TechStrap.Client.Tests.Options;

public sealed class TechStrapClientOptionsTests
{
    [Fact]
    public void Options_ToString_does_not_print_the_key()
    {
        var options = new TechStrapClientOptions { BaseAddress = new Uri("https://support.example.com/"), ApiKey = "sk_live_topsecret" };

        var text = options.ToString();

        text.ShouldContain("TechStrapClientOptions");
        text.ShouldContain("https://support.example.com");
        text.ShouldNotContain("sk_live_topsecret");
    }

    [Fact]
    public void Options_ToString_does_not_print_user_info_in_the_address()
    {
        var options = new TechStrapClientOptions { BaseAddress = new Uri("https://user:pw@h/") };

        var text = options.ToString();

        text.ShouldContain("https://h");
        text.ShouldNotContain("pw");
        text.ShouldNotContain("user");
    }

    [Fact]
    public void The_defaults_are_a_thirty_second_budget_three_attempts_and_a_half_to_five_second_backoff()
    {
        var options = new TechStrapClientOptions();

        options.BaseAddress.ShouldBeNull();
        options.ApiKey.ShouldBeNull();
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.MaxAttempts.ShouldBe(3);
        options.RetryBaseDelay.ShouldBe(TimeSpan.FromMilliseconds(500));
        options.MaxRetryDelay.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void The_client_names_and_error_codes_are_the_published_values()
    {
        TechStrapClientDefaults.HttpClientName.ShouldBe("TechStrap");
        TechStrapClientDefaults.ConfigurationSection.ShouldBe("TechStrap");
        TechStrapClientErrorCodes.InvalidApiKey.ShouldBe("invalid-api-key");
        TechStrapClientErrorCodes.UnexpectedResponse.ShouldBe("api-unexpected-response");
    }
}

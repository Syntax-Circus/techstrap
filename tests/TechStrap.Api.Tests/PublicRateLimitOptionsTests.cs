namespace TechStrap.Api.Tests;

public sealed class PublicRateLimitOptionsTests
{
    [Theory]
    [InlineData("RateLimiting:Public:PermitLimit", "0")]
    [InlineData("RateLimiting:Public:PermitLimit", "-5")]
    [InlineData("RateLimiting:Public:WindowSeconds", "0")]
    [InlineData("RateLimiting:Public:WindowSeconds", "-1")]
    public async Task A_bad_limit_fails_the_boot(string key, string value)
    {
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { [key] = value });

        var exception = Record.Exception(() => factory.CreateClient());

        exception.ShouldNotBeNull();
        exception.ToString().ShouldContain("OptionsValidationException");
        exception.ToString().ShouldContain(key);
    }

    [Fact]
    public async Task The_defaults_boot_cleanly()
    {
        await using var factory = new ApiFactory();

        Should.NotThrow(() => factory.CreateClient().Dispose());
    }
}

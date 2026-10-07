using TechStrap.Api.Options;
using TechStrap.Tests.Shared;

namespace TechStrap.Api.Tests.Intake;

public sealed class IntakeRateLimitOptionsTests
{
    [Theory]
    [InlineData("WebFormPermitLimit")]
    [InlineData("WebFormWindowSeconds")]
    [InlineData("PublicKeyPermitLimit")]
    [InlineData("PublicKeyWindowSeconds")]
    [InlineData("TrustedKeyPermitLimit")]
    [InlineData("TrustedKeyWindowSeconds")]
    public async Task A_non_positive_limit_fails_the_boot(string property)
    {
        var key = $"{IntakeRateLimitOptions.SectionName}:{property}";
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { [key] = "0" });

        var failure = StartupFailure.Capture(factory, () => factory.LogSink.Events);

        failure.Message.ShouldContain(key);
    }
}

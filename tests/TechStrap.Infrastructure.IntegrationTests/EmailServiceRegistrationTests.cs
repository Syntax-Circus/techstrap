using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SyntaxCircus.Email;
using TechStrap.Application.Email;
using TechStrap.Infrastructure.Email;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class EmailServiceRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings, bool validate = true)
    {
        settings[$"ConnectionStrings:{TechStrapDatabase.ConnectionStringName}"] = "Host=localhost;Database=unused;Username=u;Password=p";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddTechStrapPersistence();
        services.AddTechStrapEmail(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = validate, ValidateOnBuild = validate });
    }

    private static Dictionary<string, string?> Smtp() => new()
    {
        ["Email:Smtp:Host"] = "localhost",
        ["Email:Smtp:DefaultFrom"] = "noreply@techstrap.test",
    };

    [Fact]
    public void The_drain_handler_and_its_dependencies_resolve_with_scope_and_build_validation()
    {
        using var provider = Build(Smtp());
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IDrainEmailOutboxHandler>().ShouldNotBeNull();
    }

    [Fact]
    public void A_blank_powered_by_value_means_true()
    {
        var settings = Smtp();
        settings[EmailBrandingOptions.ShowPoweredByKey] = "";
        using var provider = Build(settings);

        provider.GetRequiredService<IOptions<EmailBrandingOptions>>().Value.ShowPoweredBy.ShouldBeTrue();
    }

    [Fact]
    public void A_false_powered_by_value_turns_the_line_off()
    {
        var settings = Smtp();
        settings[EmailBrandingOptions.ShowPoweredByKey] = "false";
        using var provider = Build(settings);

        provider.GetRequiredService<IOptions<EmailBrandingOptions>>().Value.ShowPoweredBy.ShouldBeFalse();
    }

    [Fact]
    public void A_powered_by_value_that_is_not_true_false_or_blank_fails_validation()
    {
        var settings = Smtp();
        settings[EmailBrandingOptions.ShowPoweredByKey] = "maybe";
        using var provider = Build(settings);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<EmailBrandingOptions>>().Value);
    }

    [Fact]
    public void An_enabled_worker_without_an_smtp_host_or_default_from_fails_validation()
    {
        using var provider = Build([]);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SmtpOptions>>().Value);
    }

    [Fact]
    public void A_disabled_worker_needs_no_smtp_settings()
    {
        using var provider = Build(new Dictionary<string, string?> { ["EmailOutbox:Enabled"] = "false" });

        provider.GetRequiredService<IOptions<SmtpOptions>>().Value.ShouldNotBeNull();
    }
}

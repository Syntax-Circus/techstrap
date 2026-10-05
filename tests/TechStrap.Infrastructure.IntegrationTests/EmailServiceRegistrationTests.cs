using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
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
        services.AddSingleton<IHostEnvironment>(new DevelopmentEnvironment());
        services.AddLogging();
        services.AddTechStrapPersistence();
        services.AddTechStrapEmail(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = validate, ValidateOnBuild = validate });
    }

    /// <summary>The persistence registration validates the connection string against the host environment, so a bare provider needs one (every real host has it).</summary>
    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "TechStrap.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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
    public void An_enabled_worker_with_smtp_host_and_default_from_resolves_the_configured_options()
    {
        using var provider = Build(Smtp());

        var options = provider.GetRequiredService<IOptions<SmtpOptions>>().Value;

        options.Host.ShouldBe("localhost");
        options.DefaultFrom.ShouldBe("noreply@techstrap.test");
    }

    [Fact]
    public void A_disabled_worker_needs_no_smtp_settings()
    {
        using var provider = Build(new Dictionary<string, string?> { ["EmailOutbox:Enabled"] = "false" });

        provider.GetRequiredService<IOptions<SmtpOptions>>().Value.ShouldNotBeNull();
    }

    [Fact]
    public void A_lease_shorter_than_one_batch_of_send_timeouts_fails_validation_and_names_the_setting()
    {
        var settings = Smtp();
        settings["Email:Smtp:TotalSendTimeout"] = "00:00:30";
        settings["EmailOutbox:BatchSize"] = "20";
        settings["EmailOutbox:LeaseSeconds"] = "120";
        using var provider = Build(settings);

        var exception = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SmtpOptions>>().Value);

        exception.Message.ShouldContain("EmailOutbox:LeaseSeconds");
        exception.Message.ShouldContain("BatchSize");
    }

    [Fact]
    public void A_lease_covering_one_batch_of_send_timeouts_plus_a_minute_passes_validation()
    {
        var settings = Smtp();
        settings["Email:Smtp:TotalSendTimeout"] = "00:00:30";
        settings["EmailOutbox:BatchSize"] = "20";
        settings["EmailOutbox:LeaseSeconds"] = "660";
        using var provider = Build(settings);

        provider.GetRequiredService<IOptions<SmtpOptions>>().Value.ShouldNotBeNull();
    }

    [Fact]
    public void The_default_lease_passes_with_the_default_batch_and_a_30_second_timeout()
    {
        var settings = Smtp();
        settings["Email:Smtp:TotalSendTimeout"] = "00:00:30";
        using var provider = Build(settings);

        provider.GetRequiredService<IOptions<SmtpOptions>>().Value.ShouldNotBeNull();
        provider.GetRequiredService<IOptions<EmailOutboxWorkerOptions>>().Value.LeaseSeconds.ShouldBe(900);
    }

    [Fact]
    public void A_disabled_worker_skips_the_lease_batch_rule()
    {
        var settings = Smtp();
        settings["EmailOutbox:Enabled"] = "false";
        settings["Email:Smtp:TotalSendTimeout"] = "00:00:30";
        settings["EmailOutbox:LeaseSeconds"] = "120";
        using var provider = Build(settings);

        provider.GetRequiredService<IOptions<SmtpOptions>>().Value.ShouldNotBeNull();
        provider.GetRequiredService<IOptions<EmailOutboxWorkerOptions>>().Value.LeaseSeconds.ShouldBe(120);
    }
}

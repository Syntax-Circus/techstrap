using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SyntaxCircus.Email;
using TechStrap.Application.Email;
using TechStrap.Application.Persistence;

namespace TechStrap.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Everything the Worker needs to drain the outbox: the SMTP sender and its adapter, the renderer, the branding and worker options,
    /// and the drain handler. SMTP settings are required only when the worker is enabled.
    /// </summary>
    public static IServiceCollection AddTechStrapEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSmtpEmailSender(configuration);
        services.TryAddSingleton<IOutboundEmailSender, SmtpOutboundEmailSender>();
        services.TryAddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

        var poweredBy = configuration[EmailBrandingOptions.ShowPoweredByKey];
        services.AddOptions<EmailBrandingOptions>()
            .Configure(options => options.ShowPoweredBy = !bool.TryParse(poweredBy, out var show) || show)
            .Validate(_ => string.IsNullOrWhiteSpace(poweredBy) || bool.TryParse(poweredBy, out var ignored),
                $"{EmailBrandingOptions.ShowPoweredByKey} must be true or false.")
            .ValidateOnStart();

        services.AddOptions<EmailOutboxWorkerOptions>()
            .Bind(configuration.GetSection(EmailOutboxWorkerOptions.SectionName))
            .Validate(options => options.PollIntervalSeconds >= 1, "EmailOutbox:PollIntervalSeconds must be >= 1.")
            .Validate(options => options.BatchSize is >= 1 and <= Paging.MaxBatchSize, $"EmailOutbox:BatchSize must be between 1 and {Paging.MaxBatchSize}.")
            .Validate(options => options.LeaseSeconds >= 30, "EmailOutbox:LeaseSeconds must be >= 30.")
            .ValidateOnStart();

        services.AddOptions<SmtpOptions>()
            .Validate<IOptions<EmailOutboxWorkerOptions>>(
                (smtp, worker) => !worker.Value.Enabled || (!string.IsNullOrWhiteSpace(smtp.Host) && !string.IsNullOrWhiteSpace(smtp.DefaultFrom)),
                "Email:Smtp:Host and Email:Smtp:DefaultFrom are required while the email outbox worker is enabled.")
            // Hangs off the SMTP options (not the worker options) so the two validators never depend on each other.
            .Validate<IOptions<EmailOutboxWorkerOptions>>(
                (smtp, worker) => LeaseCoversBatch(worker.Value, smtp),
                "EmailOutbox:LeaseSeconds must be at least EmailOutbox:BatchSize x Email:Smtp:TotalSendTimeout (in seconds) + 60, "
                + "because every claimed row shares one lease and the handler sends them one at a time; "
                + "otherwise the lease can expire mid-batch and rows are sent twice.")
            .ValidateOnStart();

        services.TryAddScoped<IDrainEmailOutboxHandler, DrainEmailOutboxHandler>();
        return services;
    }

    private static bool LeaseCoversBatch(EmailOutboxWorkerOptions worker, SmtpOptions smtp)
    {
        if (!worker.Enabled || smtp.TotalSendTimeout is not { } timeout)
        {
            return true;
        }

        return worker.LeaseSeconds >= worker.BatchSize * timeout.TotalSeconds + 60;
    }
}

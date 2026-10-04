namespace TechStrap.Admin.Features.Tickets;

public static class TicketsServiceCollectionExtensions
{
    /// <summary>Registers the ticket screen's services. Scoped: one presenter per circuit, built over that circuit's clients.</summary>
    public static IServiceCollection AddTicketFeatures(this IServiceCollection services)
    {
        services.AddScoped<TicketDetailPresenter>();
        return services;
    }
}

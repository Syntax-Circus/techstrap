namespace TechStrap.Admin.Features.Kb;

public static class KbServiceCollectionExtensions
{
    /// <summary>Registers the knowledge base screens' services. Scoped: one presenter per circuit, built over that circuit's clients.</summary>
    public static IServiceCollection AddKbFeatures(this IServiceCollection services)
    {
        services.AddScoped<KbArticleEditorPresenter>();
        return services;
    }
}

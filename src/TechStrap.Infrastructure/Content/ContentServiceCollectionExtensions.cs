using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Application.Content;

namespace TechStrap.Infrastructure.Content;

public static class ContentServiceCollectionExtensions
{
    public static IServiceCollection AddTechStrapContent(this IServiceCollection services)
    {
        services.TryAddSingleton<IHtmlSanitizer, HtmlSanitizerAdapter>();
        services.TryAddSingleton<IMarkdownRenderer, MarkdigMarkdownRenderer>();
        return services;
    }
}

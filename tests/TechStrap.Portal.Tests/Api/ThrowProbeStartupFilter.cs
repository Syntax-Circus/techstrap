using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Portal.Tests.Api;

/// <summary>
/// Throws for the paths a test names, after the whole application pipeline (like <see cref="OkProbeStartupFilter"/>), so the Production exception handler answers a real 500 under a path
/// no Portal endpoint can match.
/// </summary>
internal sealed class ThrowProbeStartupFilter(Func<PathString, bool> throws) : IStartupFilter
{
    public static Action<IServiceCollection> Add(Func<PathString, bool> throws) => services => services.AddSingleton<IStartupFilter>(new ThrowProbeStartupFilter(throws));

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Use((context, nextMiddleware) => throws(context.Request.Path) ? throw new InvalidOperationException("boom") : nextMiddleware(context));
    };
}

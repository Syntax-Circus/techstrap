using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Portal.Tests.Api;

/// <summary>
/// Answers 200 "probe" for the paths a test names, after the whole application pipeline (so only a path no Portal endpoint matched reaches it). The Portal has no 200 answer under <c>/t</c>
/// until PHASE-09b, and the header rules for a delivered page or file must be tested on a real 200 response: a probe path is chosen so that no later Portal route can match it.
/// </summary>
internal sealed class OkProbeStartupFilter(Func<PathString, bool> answers) : IStartupFilter
{
    public static Action<IServiceCollection> Add(Func<PathString, bool> answers) => services => services.AddSingleton<IStartupFilter>(new OkProbeStartupFilter(answers));

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Use(async (context, nextMiddleware) =>
        {
            if (!answers(context.Request.Path))
            {
                await nextMiddleware(context);
                return;
            }

            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("probe");
        });
    };
}

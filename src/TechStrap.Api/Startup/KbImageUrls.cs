using Microsoft.Extensions.Options;
using TechStrap.Api.Options;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Startup;

/// <summary>
/// The absolute public URL of a stored image (D-044). With <c>TECHSTRAP_API_PUBLIC_URL</c> set, the URL starts with it and the request is never
/// consulted, so a forged Host header cannot change a stored address. Only in Development, where the setting may be blank, does it fall back to the
/// origin of the current request.
/// </summary>
internal sealed class KbImageUrls(IOptions<ApiPublicUrlOptions> options, IHttpContextAccessor accessor) : IKbImageUrls
{
    public string UrlFor(string fileName)
    {
        // Built from the parsed address, never the raw string; start-up validation has already refused anything that is not an absolute http(s) URL.
        var configured = options.Value.PublicUrl.Trim();
        var baseUrl = configured.Length == 0 ? string.Empty : new Uri(configured, UriKind.Absolute).GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (baseUrl.Length == 0)
        {
            var request = accessor.HttpContext?.Request
                ?? throw new InvalidOperationException("The image URL needs TECHSTRAP_API_PUBLIC_URL or a current request.");
            baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";
        }

        return $"{baseUrl}/{KbLimits.ImagePathPrefix}{fileName}";
    }
}

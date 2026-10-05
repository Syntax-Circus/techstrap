using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Startup;

/// <summary>Request size limits for the knowledge-base endpoints (D-044). The handlers enforce the exact limits; these stop an oversize body early, as a 413.</summary>
public static class KbRequestLimits
{
    /// <summary>
    /// 2 MiB. The worst case for a 200,000-character article is 6 bytes per UTF-16 unit, because the default JSON encoder escapes non-ASCII
    /// as six-character u-escapes (1.2 MB), plus the other fields and the JSON around it.
    /// </summary>
    public const int JsonBodyBytes = 2 * 1024 * 1024;

    /// <summary>The image limit plus 1 MiB for the multipart framing, so an image over 5 MB (but not absurdly over) still reaches the handler's <c>kb-image-too-large</c> answer.</summary>
    public const long ImageFormBytes = KbLimits.MaxImageBytes + (1024 * 1024);
}

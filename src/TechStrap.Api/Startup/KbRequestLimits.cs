using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Startup;

/// <summary>Request size limits for the knowledge-base endpoints (D-044). The handlers enforce the exact limits; these stop an oversize body early, as a 413.</summary>
public static class KbRequestLimits
{
    /// <summary>1 MiB: a 200,000-character article is at most 800 KB as UTF-8, plus the JSON around it.</summary>
    public const int JsonBodyBytes = 1024 * 1024;

    /// <summary>The image limit plus 1 MiB for the multipart framing, so an image over 5 MB (but not absurdly over) still reaches the handler's <c>kb-image-too-large</c> answer.</summary>
    public const long ImageFormBytes = KbLimits.MaxImageBytes + (1024 * 1024);
}

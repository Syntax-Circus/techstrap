using SyntaxCircus.Common;
using TechStrap.Application.Content;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IRenderKbPreviewRequestHandler
{
    Task<Result<KbPreviewResponse>> HandleAsync(KbPreviewRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/kb/preview (Agent, D-021). Renders Markdown with the same <see cref="IKbContentRenderer"/> the public article page uses, so
/// the preview is exactly what readers will see. A source longer than <see cref="KbLimits.MaxPreviewChars"/> is a 400, so the endpoint
/// cannot be used as a free renderer. Nothing is stored.
/// </summary>
public sealed class RenderKbPreviewRequestHandler(IKbContentRenderer renderer) : IRenderKbPreviewRequestHandler
{
    public Task<Result<KbPreviewResponse>> HandleAsync(KbPreviewRequest request, CancellationToken cancellationToken)
    {
        var markdown = request.BodyMarkdown ?? string.Empty;
        if (markdown.Length > KbLimits.MaxPreviewChars)
        {
            return Task.FromResult(Result<KbPreviewResponse>.Failure(KbErrors.PreviewTooLong()));
        }

        // The complexity check parses but never sanitizes; the sanitizer is the expensive step. Check the token between the steps.
        cancellationToken.ThrowIfCancellationRequested();
        if (markdown.Length > 0 && renderer.IsTooComplex(markdown))
        {
            return Task.FromResult(Result<KbPreviewResponse>.Failure(KbErrors.BodyTooComplex()));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result<KbPreviewResponse>.Success(new KbPreviewResponse(markdown.Length == 0 ? string.Empty : renderer.Render(markdown))));
    }
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Features.Account;

/// <summary>
/// The optional name customers see in place of the agent's own (D-024). It shows a live preview, checks the two rules before it sends anything, and saves on blur or Enter, only when the text
/// changed since the last save (so Enter followed by blur saves once). A save is never cancelled and never retried; after it the session is asked again, and because the session keeps its
/// agent while it reloads (it never drops to "not loaded"), the page does not flicker or lose what is on it.
/// </summary>
public sealed partial class PublicDisplayNameField : IDisposable
{
    private readonly MyProfileViewModel _model = new();
    private string _committed = string.Empty;

    // The value whose save ended with an unknown outcome: it may have been stored, so the same value is never sent again from this page (until the page is reloaded or the text changes).
    private string? _uncertainValue;
    private string? _error;
    private bool _saving;
    private bool _saved;
    private bool _disposed;

    [Inject]
    private IAgentsClient Agents { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    /// <summary>The display name of the first active product, for the preview; null when there is none yet.</summary>
    [Parameter]
    public string? ProductDisplayName { get; set; }

    private string PreviewLine => PublicNamePreview.Build(_model.PublicDisplayName, Session.Agent?.Name, ProductDisplayName);

    protected override void OnInitialized()
    {
        _model.PublicDisplayName = Session.Agent?.PublicDisplayName ?? string.Empty;
        _committed = _model.Normalized ?? string.Empty;
    }

    private void OnInput(ChangeEventArgs e)
    {
        _model.PublicDisplayName = e.Value?.ToString() ?? string.Empty;
        _saved = false;
        _error = null;
    }

    private Task OnKeyDownAsync(KeyboardEventArgs e) => e.Key == "Enter" ? CommitAsync() : Task.CompletedTask;

    private async Task CommitAsync()
    {
        if (_saving)
        {
            return;
        }

        _error = _model.Check();
        if (_error is not null || (_model.Normalized ?? string.Empty) == _committed)
        {
            return;
        }

        if (_uncertainValue is not null && (_model.Normalized ?? string.Empty) == _uncertainValue)
        {
            _error = MySettingsCopy.NameUncertain;
            return;
        }

        _saving = true;
        _saved = false;
        try
        {
            var request = _model.ToRequest();
            var result = await Agents.UpdateMyProfileAsync(request, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                var error = result.Errors[0];
                _error = ApiErrorCodes.IsUncertainWrite(error.Code)
                    ? MySettingsCopy.NameUncertain
                    : error.Target == ApiFields.PublicDisplayName ? error.Message : MySettingsCopy.NameFailed(error.Message);
                if (ApiErrorCodes.IsUncertainWrite(error.Code))
                {
                    _uncertainValue = request.PublicDisplayName ?? string.Empty;
                }

                return;
            }

            _committed = request.PublicDisplayName ?? string.Empty;
            _uncertainValue = null;
            _model.PublicDisplayName = _committed;
            _saved = true;

            // The agent's own record carries the name, so ask again; the session keeps the current agent until the answer arrives.
            await Session.ReloadAsync(CancellationToken.None);
        }
        finally
        {
            _saving = false;
        }
    }

    public void Dispose() => _disposed = true;
}

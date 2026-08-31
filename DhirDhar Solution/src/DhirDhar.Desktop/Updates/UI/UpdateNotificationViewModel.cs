using System;
using System.Threading.Tasks;
using System.Windows.Input;
using DhirDhar.Application.Localization;
using DhirDhar.Desktop.Updates.Models;
using DhirDhar.Desktop.ViewModels;

using DhirDhar.Desktop.Updates.Helpers;

namespace DhirDhar.Desktop.Updates.UI;

/// <summary>
/// Binds the update notification dialog. Keeps UI-only text; no financial/DB logic here.
/// </summary>
public sealed class UpdateNotificationViewModel : ViewModelBase
{
    private readonly IUpdateService _updateService;
    private readonly ILocalizationService _localizationService;
    private string? _currentVersion;
    private string? _newVersion;
    private string? _releaseNotes;
    private bool _isApplying;

    public UpdateNotificationViewModel(IUpdateService updateService, ILocalizationService localizationService)
    {
        _updateService = updateService;
        _localizationService = localizationService;

        _localizationService.LanguageChanged += (s, e) => OnPropertyChanged(string.Empty);

        UpdateNowCommand = new RelayCommand(async () => await ApplyUpdateAsync(), () => !IsApplying);
        LaterCommand = new RelayCommand(() => { });
    }

    public RelayCommand UpdateNowCommand { get; }
    public RelayCommand LaterCommand { get; }

    public string DialogTitle => _localizationService.GetString("UpdateAvailableTitle");
    public string CurrentVersionLabel => _localizationService.GetString("CurrentVersionLabel");
    public string NewVersionLabel => _localizationService.GetString("NewVersionLabel");
    public string ReleaseNotesLabel => _localizationService.GetString("ReleaseNotesLabel");
    public string UpdateNowLabel => _localizationService.GetString("UpdateNowButton");
    public string LaterLabel => _localizationService.GetString("LaterButton");
    public string DownloadingLabel => _localizationService.GetString("DownloadingUpdate");
    public string InstallingLabel => _localizationService.GetString("InstallingUpdate");
    public string RestartingLabel => _localizationService.GetString("RestartingApplication");
    public string UpdateFailedLabel => _localizationService.GetString("UpdateFailed");

    public string? CurrentVersion
    {
        get => _currentVersion;
        set => SetProperty(ref _currentVersion, value);
    }

    public string? NewVersion
    {
        get => _newVersion;
        set => SetProperty(ref _newVersion, value);
    }

    public string? ReleaseNotes
    {
        get => _releaseNotes;
        set => SetProperty(ref _releaseNotes, value);
    }

    public bool IsApplying
    {
        get => _isApplying;
        set
        {
            if (SetProperty(ref _isApplying, value))
            {
                UpdateNowCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasError
    {
        get => !string.IsNullOrEmpty(_errorMessage);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(nameof(HasError)); }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }
    private string _statusText = string.Empty;
    private string? _errorMessage;

    public void Populate(UpdateInfo updateInfo, string installedVersion)
    {
        CurrentVersion = installedVersion;
        NewVersion = updateInfo.Version;
        ReleaseNotes = string.IsNullOrWhiteSpace(updateInfo.ReleaseNotes)
            ? _localizationService.GetString("NoUpdateAvailable")
            : updateInfo.ReleaseNotes;
        StatusText = string.Empty;
        IsApplying = false;
    }

    private async Task ApplyUpdateAsync()
    {
        if (!SemanticVersion.TryParse(CurrentVersion, out var current) || !SemanticVersion.TryParse(NewVersion, out var candidate) || candidate <= current)
        {
            return;
        }

        var updateInfo = new UpdateInfo
        {
            Version = NewVersion ?? string.Empty,
            PackageUrl = string.Empty
        };

        IsApplying = true;
        StatusText = DownloadingLabel;
        ErrorMessage = null;
        try
        {
            var available = await _updateService.CheckForUpdatesAsync(force: true).ConfigureAwait(false);
            if (available is not null)
            {
                StatusText = InstallingLabel;
                bool success = await _updateService.InstallUpdateAsync(available).ConfigureAwait(false);
                if (success)
                {
                    StatusText = RestartingLabel;
                }
                else
                {
                    StatusText = UpdateFailedLabel;
                    ErrorMessage = UpdateFailedLabel;
                }
            }
            else
            {
                StatusText = UpdateFailedLabel;
                ErrorMessage = UpdateFailedLabel;
            }
        }
        catch (Exception ex)
        {
            StatusText = UpdateFailedLabel;
            ErrorMessage = UpdateFailedLabel;
            System.Diagnostics.Debug.WriteLine($"[UPDATER] NotifyViewModel apply error: {ex.Message}");
        }
        finally
        {
            IsApplying = false;
        }
    }
}

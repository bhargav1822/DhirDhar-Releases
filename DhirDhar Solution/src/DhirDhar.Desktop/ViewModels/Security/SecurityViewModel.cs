using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using DhirDhar.Application.Security;
using DhirDhar.Desktop.ViewModels;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.ViewModels.Security;

public sealed record AutoLockOption(string Value, string Label);

public sealed class SecurityViewModel : ViewModelBase
{
    private readonly ISecurityService _securityService;
    private readonly ILogger<SecurityViewModel> _logger;

    private bool _isLockEnabled;
    private bool _isLocked;
    private string _autoLockSetting = "Never";
    private bool _isConfiguring;

    private readonly DhirDhar.Application.Localization.ILocalizationService _localizationService;

    public SecurityViewModel(
        ISecurityService securityService,
        DhirDhar.Application.Localization.ILocalizationService localizationService,
        ILogger<SecurityViewModel> logger)
    {
        _securityService = securityService;
        _localizationService = localizationService;
        _logger = logger;

        _localizationService.LanguageChanged += (s, e) => OnPropertyChanged(string.Empty);

        _isLockEnabled = securityService.IsLockEnabled;
        _isLocked = securityService.IsLocked;
        _autoLockSetting = securityService.AutoLockSetting;

        EnableLockCommand = new RelayCommand(async () => await EnableLockAsync());
        DisableLockCommand = new RelayCommand(async () => await DisableLockAsync());
        UnlockCommand = new RelayCommand(async () => await UnlockAsync());
        SetAutoLockCommand = new RelayCommand<string>(setting => _ = SetAutoLockAsync(setting));

        _securityService.LockStateChanged += OnLockStateChanged;
    }

    public bool IsLockEnabled
    {
        get => _isLockEnabled;
        set => SetProperty(ref _isLockEnabled, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        private set => SetProperty(ref _isLocked, value);
    }

    public string AutoLockSetting
    {
        get => _autoLockSetting;
        set => SetProperty(ref _autoLockSetting, value);
    }

    public bool IsConfiguring
    {
        get => _isConfiguring;
        private set => SetProperty(ref _isConfiguring, value);
    }

    public string PageTitle => _localizationService.GetString("Security");
    public string PageSubtitle => _localizationService.GetString("SecuritySubtitle");
    public string ApplicationLockLabel => _localizationService.GetString("ApplicationLock");
    public string EnableLockLabel => _localizationService.GetString("EnableLock");
    public string DisableLockLabel => _localizationService.GetString("DisableLock");
    public string AutoLockLabel => _localizationService.GetString("AutoLock");

    public ObservableCollection<AutoLockOption> AutoLockOptions => new()
    {
        new("Never", _localizationService.GetString("Never")),
        new("5 minutes", _localizationService.GetString("FiveMinutes")),
        new("10 minutes", _localizationService.GetString("TenMinutes")),
        new("15 minutes", _localizationService.GetString("FifteenMinutes")),
        new("30 minutes", _localizationService.GetString("ThirtyMinutes"))
    };

    public RelayCommand EnableLockCommand { get; }
    public RelayCommand DisableLockCommand { get; }
    public RelayCommand UnlockCommand { get; }
    public RelayCommand<string> SetAutoLockCommand { get; }

    private async Task EnableLockAsync()
    {
        IsConfiguring = true;
        try
        {
            await _securityService.EnableLockAsync("1234").ConfigureAwait(false);
            IsLockEnabled = true;
            IsLocked = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable lock.");
        }
        finally
        {
            IsConfiguring = false;
        }
    }

    private async Task DisableLockAsync()
    {
        IsConfiguring = true;
        try
        {
            await _securityService.DisableLockAsync("1234").ConfigureAwait(false);
            IsLockEnabled = false;
            IsLocked = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable lock.");
        }
        finally
        {
            IsConfiguring = false;
        }
    }

    private async Task UnlockAsync()
    {
        try
        {
            await _securityService.UnlockAsync("1234").ConfigureAwait(false);
            IsLocked = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unlock.");
        }
    }

    private async Task SetAutoLockAsync(string setting)
    {
        try
        {
            await _securityService.SetAutoLockAsync(setting).ConfigureAwait(false);
            AutoLockSetting = setting;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set auto-lock.");
        }
    }

    private void OnLockStateChanged(object? sender, EventArgs e)
    {
        IsLockEnabled = _securityService.IsLockEnabled;
        IsLocked = _securityService.IsLocked;
    }
}

using System;
using System.Threading.Tasks;
using System.Windows.Input;
using DhirDhar.Application.Licensing;
using DhirDhar.Application.Licensing.Models;
using DhirDhar.Application.Localization;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.ViewModels.License;

public sealed class LicenseViewModel : ViewModelBase
{
    private readonly ILicenseManager _licenseManager;
    private readonly ILogger<LicenseViewModel>? _logger;

    private string _serialKeyInput = string.Empty;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasError;
    private bool _isActivating;
    private bool _isSuccess;

    public event Action? ActivationSucceeded;

    public LicenseViewModel(
        ILicenseManager licenseManager,
        ILocalizationService localizationService,
        ILogger<LicenseViewModel>? logger = null)
    {
        _licenseManager = licenseManager;
        _logger = logger;

        AttachLocalization(localizationService);

        ActivateCommand = new RelayCommand(async () => await ExecuteActivateAsync(), () => CanActivate);
        PasteCommand = new RelayCommand(ExecutePaste);
        ClearCommand = new RelayCommand(ExecuteClear);

        _licenseManager.LicenseStatusChanged += OnLicenseStatusChanged;
        RefreshLicenseState();
    }

    public string DeviceId => _licenseManager.DeviceId ?? "Unknown";

    public LicenseStatus Status => _licenseManager.Status;

    public LicenseInfo? CurrentLicense => _licenseManager.CurrentLicense;

    public bool IsLicensed => _licenseManager.IsLicensed;

    public bool IsReadOnly => _licenseManager.IsReadOnly;

    public bool RequiresActivation => _licenseManager.RequiresActivation;

    public string FormattedStatus => Status switch
    {
        LicenseStatus.Active => GetString("LicenseActive"),
        LicenseStatus.ExpiringSoon => GetString("LicenseExpiringSoon"),
        LicenseStatus.Expired => GetString("LicenseExpired"),
        LicenseStatus.Invalid => GetString("LicenseInvalid"),
        _ => GetString("LicenseNotActivated")
    };

    public string AnnualOfflineLicenseActivationLabel => GetString("AnnualOfflineLicenseActivation");
    public string LicenseActivationInstructionsLabel => GetString("LicenseActivationInstructions");
    public string SerialKeyLabel => GetString("SerialKey");
    public string PasteFromClipboardLabel => GetString("PasteFromClipboard");
    public string PasteTooltipLabel => GetString("PasteTooltip");
    public string ActivateLicenseLabel => GetString("ActivateLicense");
    public string ClearLabel => GetString("Clear");
    public string OfflineNoticeLabel => GetString("OfflineNotice");
    public string HardwareIdLabel => GetString("HardwareIdLabel");
    public string SupportTitleLabel => GetString("SupportTitle");
    public string SupportDescriptionLabel => GetString("SupportDescription");
    public string RenewLicenseTitleLabel => GetString("RenewLicenseTitle");
    public string RenewLicenseInstructionsLabel => GetString("RenewLicenseInstructions");
    public string ActivateNewKeyLabel => GetString("ActivateNewKey");
    public string CancelLabel => GetString("Cancel");
    public string ActivationFailedLabel => GetString("ActivationFailed");
    public string LicenseActivatedLabel => GetString("LicenseActivated");

    public string SerialKeyInput
    {
        get => _serialKeyInput;
        set
        {
            var cleaned = value?.Trim().ToUpperInvariant() ?? string.Empty;
            if (SetProperty(ref _serialKeyInput, cleaned))
            {
                HasError = false;
                ErrorMessage = string.Empty;
                OnPropertyChanged(nameof(CanActivate));
                ((RelayCommand)ActivateCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public bool IsActivating
    {
        get => _isActivating;
        private set
        {
            if (SetProperty(ref _isActivating, value))
            {
                OnPropertyChanged(nameof(CanActivate));
                ((RelayCommand)ActivateCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSuccess
    {
        get => _isSuccess;
        private set => SetProperty(ref _isSuccess, value);
    }

    public bool CanActivate => !string.IsNullOrWhiteSpace(SerialKeyInput) && !IsActivating;

    public ICommand ActivateCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand ClearCommand { get; }

    public async Task<bool> ExecuteActivateAsync()
    {
        var keyToActivate = SerialKeyInput?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(keyToActivate) || IsActivating)
        {
            return false;
        }

        try
        {
            IsActivating = true;
            HasError = false;
            ErrorMessage = string.Empty;
            StatusMessage = "Verifying cryptographic digital signature offline...";

            var result = await _licenseManager.ActivateAsync(keyToActivate);

            if (result.Success)
            {
                IsSuccess = true;
                StatusMessage = $"License activated successfully for {result.LicenseInfo?.CustomerName} (Valid until {result.LicenseInfo?.FormattedExpiresAt}).";
                _logger?.LogInformation("License activation successful in ViewModel.");
                RefreshLicenseState();
                ActivationSucceeded?.Invoke();
                return true;
            }
            else
            {
                HasError = true;
                ErrorMessage = result.Message;
                StatusMessage = string.Empty;
                _logger?.LogWarning("License activation failed in ViewModel: {Message}", result.Message);
                return false;
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Activation failed: {ex.Message}";
            StatusMessage = string.Empty;
            _logger?.LogError(ex, "Exception during license activation in ViewModel.");
            return false;
        }
        finally
        {
            IsActivating = false;
        }
    }

    public async Task<bool> ExecuteRenewAsync(string newKey)
    {
        SerialKeyInput = newKey;
        return await ExecuteActivateAsync();
    }

    private void ExecutePaste()
    {
        try
        {
            var package = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (package.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                var textTask = package.GetTextAsync();
                textTask.Completed = (info, status) =>
                {
                    if (status == Windows.Foundation.AsyncStatus.Completed)
                    {
                        var text = info.GetResults();
                        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher = App.MainDispatcherQueue;
                        if (dispatcher == null)
                        {
                            try
                            {
                                dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                            }
                            catch { }
                        }

                        if (dispatcher != null && !dispatcher.HasThreadAccess)
                        {
                            dispatcher.TryEnqueue(() =>
                            {
                                SerialKeyInput = text ?? string.Empty;
                            });
                        }
                        else
                        {
                            SerialKeyInput = text ?? string.Empty;
                        }
                    }
                };
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to paste from clipboard.");
        }
    }

    private void ExecuteClear()
    {
        SerialKeyInput = string.Empty;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(CanActivate));
        ((RelayCommand)ActivateCommand).RaiseCanExecuteChanged();
    }

    private void OnLicenseStatusChanged(object? sender, LicenseStatus status)
    {
        RefreshLicenseState();
    }

    private void RefreshLicenseState()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(FormattedStatus));
        OnPropertyChanged(nameof(CurrentLicense));
        OnPropertyChanged(nameof(IsLicensed));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(RequiresActivation));
        OnPropertyChanged(nameof(DeviceId));
    }
}

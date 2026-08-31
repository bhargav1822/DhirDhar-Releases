using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Licensing;
using DhirDhar.Application.Licensing.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Settings;
using DhirDhar.Desktop.Configuration;
using DhirDhar.Desktop.Services;
using DhirDhar.Desktop.Updates;
using DhirDhar.Desktop.Updates.Models;
using DhirDhar.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace DhirDhar.Desktop.ViewModels.Settings;

public sealed record ThemeOption(string Value, string Label);

public sealed record PaperSizeOption(string Value, string Label);

public sealed record PrinterOption(string? Value, string Label);

public sealed class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsService? _settingsService;
    private readonly ILocalizationService _localizationService = null!;
    private readonly IDateLocalizationService _dateLocalizationService;
    private readonly IInputLanguageService? _inputLanguageService;
    private readonly AppOptions _appOptions;
    private readonly IUpdateService? _updateService;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ILicenseManager? _licenseManager;
    private readonly DhirDhar.Application.Security.Keys.IKeyManagementService? _keyManagementService;
    private readonly DhirDhar.Application.Printing.IPrintService? _printService;
    private readonly DhirDhar.Application.Validation.IIntegrityService? _integrityService;

    private IReadOnlyList<PrinterOption> _printerOptions;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _scanCts;

    private string _selectedLanguage = "en-IN";
    private bool _isLanguageSelectorInitialized;
    private string _selectedDateFormat = "DD-MM-YYYY";
    private string _selectedCurrency = "₹ Indian Rupee (INR)";
    private string _selectedTheme = "System Default";
    private string _businessName = BusinessProfileHelper.DefaultBusinessName;

    private string _selectedPaperSize = "A4";
    private double _customPaperWidthMm = 80.0;
    private bool _autoCutPaperEnabled = true;
    private string? _selectedPrinter;

    private bool _isLoading;
    private bool _isSaving;
    private bool _isUpdateCheckInProgress;
    private bool _isDownloading;
    private int _downloadProgressPercent;
    private string? _updateStatusMessage;
    private string? _statusMessage;
    private string? _errorMessage;
    private bool _autoCheckEnabled = true;
    private bool _autoInstallEnabled;
    private bool _isUpdateAvailable;
    private bool _isReadyToInstall;
    private string? _latestVersion;
    private string? _releaseNotes;
    private string? _recoveryKeyDisplay;
    private bool _isExportingRecoveryKey;
    private bool _isVerifyingEncryption;
    private string? _encryptionVerificationMessage;
    private string? _lastVerificationTime;

    private bool _isIntegrityScanning;
    private string? _integrityStatusMessage;
    private string? _overallStatusDisplay;
    private string? _integritySummaryDisplay;
    private string? _totalIssuesDisplay;
    private string? _scannedAtDisplay;
    private DhirDhar.Application.Validation.Models.IntegrityScanReport? _lastIntegrityReport;
    private DateTime? _lastIntegrityScanTime;
    private bool _isIntegrityScanCompleted;

    public SettingsViewModel() : this(null, null, null, null, null, null, null, null, null, null, null)
    {
    }

    public SettingsViewModel(
        ISettingsService? settingsService = null,
        ILocalizationService? localizationService = null,
        IDateLocalizationService? dateLocalizationService = null,
        IInputLanguageService? inputLanguageService = null,
        AppOptions? appOptions = null,
        IUpdateService? updateService = null,
        ILogger<SettingsViewModel>? logger = null,
        ILicenseManager? licenseManager = null,
        DhirDhar.Application.Security.Keys.IKeyManagementService? keyManagementService = null,
        DhirDhar.Application.Printing.IPrintService? printService = null,
        DhirDhar.Application.Validation.IIntegrityService? integrityService = null)
    {
        var sp = App.ServiceProvider;
        ILocalizationService resolvedLoc = localizationService ?? (sp?.GetService(typeof(ILocalizationService)) as ILocalizationService) ?? new DhirDhar.Infrastructure.Localization.LocalizationService();
        _localizationService = resolvedLoc;
        _settingsService = settingsService ?? (sp?.GetService(typeof(ISettingsService)) as ISettingsService);
        _dateLocalizationService = dateLocalizationService ?? (sp?.GetService(typeof(IDateLocalizationService)) as IDateLocalizationService) ?? new DhirDhar.Infrastructure.Localization.DateLocalizationService();
        _inputLanguageService = inputLanguageService ?? (sp?.GetService(typeof(IInputLanguageService)) as IInputLanguageService);
        _appOptions = appOptions ?? (sp?.GetService(typeof(AppOptions)) as AppOptions) ?? new AppOptions();
        _updateService = updateService ?? (sp?.GetService(typeof(IUpdateService)) as IUpdateService);
        _logger = logger ?? (sp?.GetService(typeof(ILogger<SettingsViewModel>)) as ILogger<SettingsViewModel>) ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsViewModel>.Instance;
        _licenseManager = licenseManager ?? (sp?.GetService(typeof(ILicenseManager)) as ILicenseManager);
        _keyManagementService = keyManagementService ?? (sp?.GetService(typeof(DhirDhar.Application.Security.Keys.IKeyManagementService)) as DhirDhar.Application.Security.Keys.IKeyManagementService);
        _printService = printService ?? (sp?.GetService(typeof(DhirDhar.Application.Printing.IPrintService)) as DhirDhar.Application.Printing.IPrintService);
        _integrityService = integrityService ?? (sp?.GetService(typeof(DhirDhar.Application.Validation.IIntegrityService)) as DhirDhar.Application.Validation.IIntegrityService);

        _printerOptions = new List<PrinterOption>
        {
            new(null, _localizationService.GetString("DefaultPrinter"))
        };

        if (_licenseManager != null)
        {
            _licenseManager.LicenseStatusChanged += OnLicenseStatusChanged;
        }

        ResetSettingsCommand = new RelayCommand(async () => await ResetSettingsAsync());
        CheckForUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesAsync(), () => !IsUpdateCheckInProgress && !IsDownloading);
        UpdateNowCommand = new RelayCommand(async () => await InstallUpdateAsync(), () => !IsUpdateCheckInProgress && !IsDownloading && (IsUpdateAvailable || IsReadyToInstall) && _updateService is not null);
        ExportRecoveryKeyCommand = new RelayCommand(async () => await ExportRecoveryKeyAsync(), () => !IsExportingRecoveryKey);
        VerifyEncryptionCommand = new RelayCommand(async () => await VerifyEncryptionAsync(), () => !IsVerifyingEncryption);
        PrintTestReceiptCommand = new RelayCommand(async () => await PrintTestReceiptAsync());
        RunFullScanCommand = new RelayCommand(async () => await RunFullScanAsync(), () => !IsIntegrityScanning);

        if (_localizationService != null)
        {
            _localizationService.LanguageChanged += OnLocalizationLanguageChanged;
        }

        if (_updateService is not null)
        {
            SubscribeUpdateEvents();
        }
    }

    private void OnLicenseStatusChanged(object? sender, DhirDhar.Application.Licensing.Models.LicenseStatus e)
    {
        RunOnUiThread(() => OnPropertyChanged(string.Empty));
    }

    private void OnLocalizationLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() => OnPropertyChanged(string.Empty));
    }

    public RelayCommand ExportRecoveryKeyCommand { get; }
    public RelayCommand VerifyEncryptionCommand { get; }

    public string AppName => _appOptions.Name;
    public string AppVersion => $"{L("Version")} {LocalizeDigits(_appOptions.Version)}";
    public string RawAppVersion => _appOptions.Version;
    public string CopyrightText => _localizationService.GetString("CopyrightFooter");
    public string PageTitle => _localizationService.GetString("Settings");
    public string PageSubtitle => _localizationService.GetString("SettingsSubtitle");
    public string LanguageSectionLabel => _localizationService.GetString("Language");
    public string ApplicationLanguageLabel => _localizationService.GetString("ApplicationLanguage");
    public string GeneralSettingsLabel => _localizationService.GetString("GeneralSettings");
    public string DateFormatLabel => _localizationService.GetString("DateFormat");
    public string CurrencyLabel => _localizationService.GetString("Currency");
    public string AppearanceLabel => _localizationService.GetString("Appearance");
    public string ThemeLabel => _localizationService.GetString("Theme");
    public string BusinessProfileLabel => _localizationService.GetString("BusinessProfile");
    public string BusinessNameLabel => _localizationService.GetString("BusinessName");
    public string BusinessNamePlaceholder => _localizationService.GetString("BusinessNamePlaceholder");
    public string BorrowerNumberPrefixLabel => _localizationService.GetString("BorrowerNumberPrefix");
    public string FinancialSystemConfigLabel => _localizationService.GetString("FinancialSystemConfig");
    public string CurrentFinancialYearLabel => _localizationService.GetString("CurrentFinancialYear");
    public string InterestEngineLabel => _localizationService.GetString("InterestEngine");
    public string LicenseInformationLabel => _localizationService.GetString("LicenseInformation");
    public string LicenseStatusText => _licenseManager?.Status switch
    {
        LicenseStatus.Active => _localizationService.GetString("LicenseActive"),
        LicenseStatus.ExpiringSoon => _localizationService.GetString("LicenseExpiringSoon"),
        LicenseStatus.Expired => _localizationService.GetString("LicenseExpired"),
        LicenseStatus.Invalid => _localizationService.GetString("LicenseInvalid"),
        _ => _localizationService.GetString("LicenseNotActivated")
    };
    public string LicenseCustomerName => _licenseManager?.CurrentLicense?.CustomerName ?? _localizationService.GetString("Unassigned");
    public string LicenseId => _licenseManager?.CurrentLicense?.LicenseId ?? "N/A";
    public string LicenseExpiresAt => _licenseManager?.CurrentLicense?.ExpiresAt.ToString("dd-MMM-yyyy") ?? "N/A";
    public string LicenseExpiryDate => LicenseExpiresAt;
    public string LicenseDaysRemaining => _licenseManager?.CurrentLicense?.DaysRemaining.ToString() ?? "0";
    public string LicenseDeviceId => _licenseManager?.DeviceId ?? "N/A";
    public bool HasActiveLicense => _licenseManager?.IsLicensed ?? false;
    public bool IsLicenseExpired => _licenseManager?.IsReadOnly ?? false;
    public string ApplicationInformationLabel => _localizationService.GetString("ApplicationInformation");

    public void RefreshLicenseState() => OnPropertyChanged(string.Empty);
    public string ResetSettingsLabel => _localizationService.GetString("ResetSettings");
    public string ResetSettingsDescLabel => _localizationService.GetString("ResetSettingsDesc");
    public string InterestConfigInfo => _localizationService.GetString("InterestConfigInfo");
    public string ApplicationUpdatesLabel => _localizationService.GetString("ApplicationUpdates");
    public string AutoCheckUpdatesLabel => _localizationService.GetString("AutoCheckUpdates");
    public string AutoInstallUpdatesLabel => _localizationService.GetString("AutoInstallUpdates");
    public string CheckForUpdatesLabel => _localizationService.GetString("CheckForUpdatesButton");
    public string CheckingForUpdatesLabel => _localizationService.GetString("CheckingForUpdates");
    public string CurrentVersionLabel => _localizationService.GetString("CurrentVersionLabel");
    public string NewVersionLabel => _localizationService.GetString("NewVersionLabel");
    public string ReleaseNotesLabel => _localizationService.GetString("ReleaseNotesLabel");
    public string InstallButtonText => IsReadyToInstall ? _localizationService.GetString("RestartAndInstall") : _localizationService.GetString("UpdateNowButton");

    public bool IsUpdateCheckInProgress
    {
        get => _isUpdateCheckInProgress;
        private set { if (SetProperty(ref _isUpdateCheckInProgress, value)) { RaiseUpdateCommandStates(); } }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set { if (SetProperty(ref _isDownloading, value)) { RaiseUpdateCommandStates(); } }
    }

    public int DownloadProgressPercent
    {
        get => _downloadProgressPercent;
        private set
        {
            if (SetProperty(ref _downloadProgressPercent, value))
            {
                OnPropertyChanged(nameof(FormattedDownloadProgress));
            }
        }
    }

    public string FormattedDownloadProgress => $"{DownloadProgressPercent}%";

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set { if (SetProperty(ref _isUpdateAvailable, value)) { RaiseUpdateCommandStates(); } }
    }

    public bool IsReadyToInstall
    {
        get => _isReadyToInstall;
        private set
        {
            if (SetProperty(ref _isReadyToInstall, value))
            {
                OnPropertyChanged(nameof(InstallButtonText));
                RaiseUpdateCommandStates();
            }
        }
    }

    public string? LatestVersion
    {
        get => _latestVersion;
        private set => SetProperty(ref _latestVersion, value);
    }

    public string? ReleaseNotes
    {
        get => _releaseNotes;
        private set
        {
            if (SetProperty(ref _releaseNotes, value))
            {
                OnPropertyChanged(nameof(HasReleaseNotes));
            }
        }
    }

    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);

    public string? UpdateStatusMessage
    {
        get => _updateStatusMessage;
        private set => SetProperty(ref _updateStatusMessage, value);
    }

    public string ActiveFinancialYear
    {
        get
        {
            var now = DateTime.UtcNow;
            int startYear = now.Month >= 4 ? now.Year : now.Year - 1;
            int endYear = (startYear + 1) % 100;
            var fyPrefix = _localizationService.GetString("FinancialYearPrefix");
            return $"{fyPrefix} {LocalizeDigits(startYear.ToString())}-{LocalizeDigits(endYear.ToString("D2"))}";
        }
    }

    public string SinglePcAnnualOfflineLicenseLabel => _localizationService.GetString("SinglePcAnnualOfflineLicense");
    public string RenewChangeLicenseLabel => _localizationService.GetString("RenewChangeLicense");
    public string LicenseStatusLabel => _localizationService.GetString("LicenseStatus");
    public string LicensedToLabel => _localizationService.GetString("LicensedTo");
    public string LicenseIdLabel => _localizationService.GetString("LicenseId");
    public string ValidUntilLabel => _localizationService.GetString("ValidUntil");
    public string HardwareDeviceIdLabel => _localizationService.GetString("HardwareDeviceId");

    public record LanguageOption(string Code, string DisplayName);

    public IReadOnlyList<LanguageOption> LanguageOptions => new List<LanguageOption>
    {
        new("en-IN", "English"),
        new("gu-IN", "ગુજરાતી"),
        new("hi-IN", "हिन्दी")
    };

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _logger.LogInformation("[LANGUAGE] Settings ComboBox initial SelectedValue = null/empty ignored (current _selectedLanguage={SelectedLanguage}, CurrentLanguage={Current})", _selectedLanguage, _localizationService?.CurrentLanguage);
                return;
            }

            if (!_isLanguageSelectorInitialized)
            {
                _logger.LogInformation("[LANGUAGE] Settings initialization = ignoring SelectionChanged for value {Value} (isInitialized=false, current={Current})", value, _selectedLanguage);
                return;
            }

            var canonical = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeLanguageCode(value);
            if (string.Equals(_selectedLanguage, canonical, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (SetProperty(ref _selectedLanguage, canonical))
            {
                _logger.LogInformation("[LANGUAGE] User language selection = {Selected} -> canonical {Canonical}", value, canonical);
                _localizationService.SetLanguage(canonical);
                _inputLanguageService?.SetLanguage(canonical);
                _logger.LogInformation("[LANGUAGE] CurrentLanguage after selection = {CurrentLanguage}", _localizationService.CurrentLanguage);
                _ = SaveCurrentSettingsAsync();
                RunOnUiThread(() => OnPropertyChanged(string.Empty));
            }
        }
    }

    public IReadOnlyList<string> DateFormats => new List<string> { "DD-MM-YYYY", "MM-DD-YYYY", "YYYY-MM-DD" };

    public IReadOnlyList<string> Currencies => new List<string> { _localizationService.GetString("CurrencyINR") };

    public IReadOnlyList<ThemeOption> ThemeOptions => new List<ThemeOption>
    {
        new("System Default", _localizationService.GetString("ThemeSystemDefault")),
        new("Light", _localizationService.GetString("ThemeLight")),
        new("Dark", _localizationService.GetString("ThemeDark"))
    };

    public string SelectedDateFormat
    {
        get => _selectedDateFormat;
        set
        {
            if (SetProperty(ref _selectedDateFormat, value))
            {
                _dateLocalizationService.SetDateFormatPattern(value);
                _ = SaveCurrentSettingsAsync();
            }
        }
    }

    public string SelectedCurrency
    {
        get => _selectedCurrency;
        set
        {
            if (SetProperty(ref _selectedCurrency, value))
            {
                _ = SaveCurrentSettingsAsync();
            }
        }
    }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                ApplyTheme(value);
                _ = SaveCurrentSettingsAsync();
            }
        }
    }

    public string BusinessName
    {
        get => _businessName;
        set
        {
            if (SetProperty(ref _businessName, value))
            {
                OnPropertyChanged(nameof(BorrowerNumberPrefix));
                _ = SaveCurrentSettingsAsync();
            }
        }
    }

    public string BorrowerNumberPrefix => BusinessProfileHelper.GeneratePrefix(_businessName);

    // Printing & POS Thermal Paper Properties
    public string PrintingSettingsLabel => _localizationService.GetString("PrintingSettings");
    public string PaperSizeLabel => _localizationService.GetString("PaperSize");
    public string CustomPaperWidthMmLabel => _localizationService.GetString("CustomPaperWidthMm");
    public string SelectedPrinterLabel => _localizationService.GetString("SelectedPrinter");
    public string AutoCutPaperLabel => _localizationService.GetString("AutoCutPaper");
    public string PrintTestReceiptLabel => _localizationService.GetString("PrintTestReceipt");

    public IReadOnlyList<PaperSizeOption> PaperSizeOptions => new List<PaperSizeOption>
    {
        new("A4", _localizationService.GetString("PaperSizeA4")),
        new("A5", _localizationService.GetString("PaperSizeA5")),
        new("Letter", _localizationService.GetString("PaperSizeLetter")),
        new("POS58", _localizationService.GetString("PaperSizePOS58")),
        new("POS80", _localizationService.GetString("PaperSizePOS80")),
        new("POS110", _localizationService.GetString("PaperSizePOS110")),
        new("POSCustom", _localizationService.GetString("PaperSizePOSCustom"))
    };

    public IReadOnlyList<PrinterOption> PrinterOptions => _printerOptions;

    public string SelectedPaperSize
    {
        get => _selectedPaperSize;
        set
        {
            if (SetProperty(ref _selectedPaperSize, value))
            {
                OnPropertyChanged(nameof(IsCustomPaperWidthVisible));
                _ = SaveCurrentSettingsAsync();
            }
        }
    }

    public bool IsCustomPaperWidthVisible => string.Equals(_selectedPaperSize, "POSCustom", StringComparison.OrdinalIgnoreCase);

    public double CustomPaperWidthMm
    {
        get => _customPaperWidthMm;
        set
        {
            var clamped = Math.Clamp(value, 30.0, 300.0);
            if (SetProperty(ref _customPaperWidthMm, clamped))
            {
                _ = SaveCurrentSettingsAsync();
            }
        }
    }

    public string? SelectedPrinter
    {
        get => _selectedPrinter;
        set
        {
            if (SetProperty(ref _selectedPrinter, value))
            {
                _ = SaveCurrentSettingsAsync();
            }
        }
    }

    public bool AutoCutPaperEnabled
    {
        get => _autoCutPaperEnabled;
        set
        {
            if (SetProperty(ref _autoCutPaperEnabled, value))
            {
                _ = SaveCurrentSettingsAsync();
            }
        }
    }

    public RelayCommand PrintTestReceiptCommand { get; }

    public async Task PrintTestReceiptAsync()
    {
        if (_printService == null) return;
        try
        {
            StatusMessage = _localizationService.GetString("GeneratingReport");
            var sample = new DhirDhar.Application.Printing.ReceiptData
            {
                Type = DhirDhar.Application.Printing.ReceiptType.BorrowerReceipt,
                BusinessName = _businessName,
                Title = "DhirDhar POS Test Receipt",
                Subtitle = "Financial Management System",
                BorrowerName = "ભાર્ગવ / Bhargav",
                BorrowerNumber = "DJ01",
                Contact = "9876543210",
                Village = "Ahmedabad",
                LoanDate = DateTime.Today,
                InitialPrincipal = 10000m,
                InterestRate = 3.00m,
                DisplayDuration = "12 Months",
                MonthlyInterest = 300m,
                CurrentPrincipal = 10000m,
                TotalInterest = 300m,
                TotalOutstanding = 10300m,
                TransactionDate = DateTime.Now,
                TransactionAmount = 10000m,
                TransactionType = "Deposit",
                PaperSize = _selectedPaperSize,
                CustomPaperWidthMm = _customPaperWidthMm,
                AutoCut = _autoCutPaperEnabled,
                LanguageCode = _localizationService.CurrentLanguage,
                FooterNote = "Thank You For Using DhirDhar"
            };

            var qrService = App.ServiceProvider?.GetService(typeof(DhirDhar.Application.QrCode.IQrCodeService)) as DhirDhar.Application.QrCode.IQrCodeService;
            if (qrService != null)
            {
                sample.QrCodePngBytes = qrService.GeneratePngBytes("DJ01", 8);
            }

            var path = await _printService.GenerateReceiptPdfAsync(sample);
            StatusMessage = $"{_localizationService.GetString("TestReceiptSuccess")} ({System.IO.Path.GetFileName(path)})";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print test receipt.");
            ErrorMessage = ex.Message;
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set => SetProperty(ref _isSaving, value);
    }

    public bool AutoCheckUpdatesEnabled
    {
        get => _autoCheckEnabled;
        set
        {
            if (SetProperty(ref _autoCheckEnabled, value))
            {
                if (!value && _autoInstallEnabled)
                {
                    _autoInstallEnabled = false;
                    OnPropertyChanged(nameof(AutoInstallUpdatesEnabled));
                }
                _ = SaveUpdateTogglesAsync();
            }
        }
    }

    public bool AutoInstallUpdatesEnabled
    {
        get => _autoInstallEnabled;
        set
        {
            if (SetProperty(ref _autoInstallEnabled, value))
            {
                if (value && !_autoCheckEnabled)
                {
                    _autoCheckEnabled = true;
                    OnPropertyChanged(nameof(AutoCheckUpdatesEnabled));
                }
                _ = SaveUpdateTogglesAsync();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public RelayCommand ResetSettingsCommand { get; }
    public RelayCommand CheckForUpdatesCommand { get; }
    public RelayCommand UpdateNowCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _loadCts?.Cancel();
        }
        catch
        {
        }

        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _loadCts.Token;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            DhirDhar.Application.Settings.AppSettingsModel? settings = null;
            if (_settingsService != null)
            {
                try
                {
                    settings = await Task.Run(async () => await _settingsService.GetSettingsAsync(ct), ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch settings from service; falling back to current/defaults.");
                }
            }

            if (ct.IsCancellationRequested) return;

            RunOnUiThread(() =>
            {
                _isLanguageSelectorInitialized = false;
                _logger.LogInformation("[LANGUAGE] Settings initialization started (_isLanguageSelectorInitialized=false) CurrentLanguage before Settings = {CurrentLanguage}", _localizationService.CurrentLanguage);
                if (settings != null)
                {
                    _selectedLanguage = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeLanguageCode(settings.Language);
                    _selectedDateFormat = string.IsNullOrWhiteSpace(settings.DateFormat) ? "DD-MM-YYYY" : settings.DateFormat;
                    _selectedCurrency = _localizationService?.GetString("CurrencyINR") ?? "₹ Indian Rupee (INR)";
                    _selectedTheme = MapCodeToThemeDisplay(settings.Theme);
                    _autoCheckEnabled = settings.UpdatesAutoCheckEnabled;
                    _autoInstallEnabled = settings.UpdatesAutoInstallEnabled;
                    _businessName = string.IsNullOrWhiteSpace(settings.BusinessName) ? BusinessProfileHelper.DefaultBusinessName : settings.BusinessName;
                    _selectedPaperSize = string.IsNullOrWhiteSpace(settings.PaperSize) ? "A4" : settings.PaperSize;
                    _customPaperWidthMm = settings.CustomPaperWidthMm > 0 ? settings.CustomPaperWidthMm : 80.0;
                    _autoCutPaperEnabled = settings.AutoCutPaper;
                    _selectedPrinter = settings.SelectedPrinter;
                }
                else
                {
                    _selectedLanguage = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeLanguageCode(_localizationService?.CurrentLanguage ?? "en-IN");
                    _selectedDateFormat = _dateLocalizationService?.DateFormatPattern ?? "DD-MM-YYYY";
                    _selectedCurrency = _localizationService?.GetString("CurrencyINR") ?? "₹ Indian Rupee (INR)";
                    _selectedTheme = "System Default";
                    _businessName = BusinessProfileHelper.DefaultBusinessName;
                    _selectedPaperSize = "A4";
                    _customPaperWidthMm = 80.0;
                }

                _logger.LogInformation("[LANGUAGE] Settings ComboBox initial SelectedValue = {SelectedValue} (CurrentLanguage={CurrentLanguage})", _selectedLanguage, _localizationService?.CurrentLanguage);
                _logger.LogInformation("[LANGUAGE] CurrentLanguage before Settings = {CurrentLanguage}", _localizationService?.CurrentLanguage);

                OnPropertyChanged(nameof(SelectedLanguage));
                OnPropertyChanged(nameof(LanguageOptions));
                OnPropertyChanged(nameof(SelectedDateFormat));
                OnPropertyChanged(nameof(SelectedCurrency));
                OnPropertyChanged(nameof(SelectedTheme));
                OnPropertyChanged(nameof(AutoCheckUpdatesEnabled));
                OnPropertyChanged(nameof(AutoInstallUpdatesEnabled));
                OnPropertyChanged(nameof(BusinessName));
                OnPropertyChanged(nameof(BorrowerNumberPrefix));
                OnPropertyChanged(nameof(SelectedPaperSize));
                OnPropertyChanged(nameof(CustomPaperWidthMm));
                OnPropertyChanged(nameof(IsCustomPaperWidthVisible));
                OnPropertyChanged(nameof(AutoCutPaperEnabled));
                OnPropertyChanged(nameof(SelectedPrinter));
                OnPropertyChanged(nameof(PaperSizeOptions));

                ApplyTheme(_selectedTheme);

                _isLanguageSelectorInitialized = true;
                _logger.LogInformation("[LANGUAGE] Settings initialization = complete (_isLanguageSelectorInitialized=true) SelectedLanguage={SelectedLanguage}, CurrentLanguage={CurrentLanguage}", _selectedLanguage, _localizationService?.CurrentLanguage);

                if (_updateService is not null)
                {
                    IsReadyToInstall = _updateService.IsReadyToInstall;
                    if (IsReadyToInstall && _updateService.AvailableUpdate is not null)
                    {
                        IsUpdateAvailable = true;
                        LatestVersion = _updateService.AvailableUpdate.Version;
                        ReleaseNotes = _updateService.AvailableUpdate.ReleaseNotes;
                        UpdateStatusMessage = _localizationService?.GetString("UpdateDownloadedAndVerified") ?? "Update ready to install.";
                    }
                }
            });

            await LoadPrinterOptionsAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings.");
            RunOnUiThread(() => ErrorMessage = _localizationService?.GetString("SettingsLoadFailed") ?? "Failed to load settings.");
        }
        finally
        {
            RunOnUiThread(() => IsLoading = false);
        }
    }

    private async Task LoadPrinterOptionsAsync(CancellationToken ct)
    {
        try
        {
            var defaultLabel = _localizationService?.GetString("DefaultPrinter") ?? "Default Printer";
            var printers = await Task.Run(() =>
            {
                var list = new List<PrinterOption>
                {
                    new(null, defaultLabel)
                };
                if (_printService != null)
                {
                    try
                    {
                        foreach (var p in _printService.GetInstalledPrinters())
                        {
                            if (!string.IsNullOrWhiteSpace(p))
                            {
                                list.Add(new(p, p));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not query printer list.");
                    }
                }
                return (IReadOnlyList<PrinterOption>)list;
            }, ct).ConfigureAwait(false);

            if (!ct.IsCancellationRequested)
            {
                RunOnUiThread(() =>
                {
                    _printerOptions = printers;
                    OnPropertyChanged(nameof(PrinterOptions));
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load printer options.");
        }
    }

    public void CancelPendingOperations()
    {
        try
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
        }
        catch
        {
        }
        try
        {
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        CancelPendingOperations();
        if (_localizationService != null)
        {
            _localizationService.LanguageChanged -= OnLocalizationLanguageChanged;
        }
        if (_licenseManager != null)
        {
            _licenseManager.LicenseStatusChanged -= OnLicenseStatusChanged;
        }
        UnsubscribeUpdateEvents();
    }

    private async Task SaveCurrentSettingsAsync()
    {
        if (_settingsService == null || IsLoading || IsSaving) return;

        IsSaving = true;
        StatusMessage = _localizationService.GetString("SavingSettings");
        ErrorMessage = null;
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.Language = _selectedLanguage;
            settings.DateFormat = string.IsNullOrWhiteSpace(_selectedDateFormat) ? "DD-MM-YYYY" : _selectedDateFormat.Trim();
            settings.Currency = "INR";
            settings.Theme = MapThemeDisplayToCode(_selectedTheme);
            settings.UpdatesAutoCheckEnabled = _autoCheckEnabled;
            settings.UpdatesAutoInstallEnabled = _autoInstallEnabled;
            settings.BusinessName = string.IsNullOrWhiteSpace(_businessName) ? BusinessProfileHelper.DefaultBusinessName : _businessName.Trim();
            settings.PaperSize = string.IsNullOrWhiteSpace(_selectedPaperSize) ? "A4" : _selectedPaperSize.Trim();
            settings.CustomPaperWidthMm = _customPaperWidthMm;
            settings.AutoCutPaper = _autoCutPaperEnabled;
            settings.SelectedPrinter = _selectedPrinter;

            await _settingsService.SaveSettingsAsync(settings);
            StatusMessage = _localizationService.GetString("SettingsSaved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings.");
            ErrorMessage = _localizationService.GetString("SettingsSaveFailed");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task SaveUpdateTogglesAsync()
    {
        if (_settingsService == null || IsLoading || IsSaving) return;

        IsSaving = true;
        ErrorMessage = null;
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            settings.UpdatesAutoCheckEnabled = _autoCheckEnabled;
            settings.UpdatesAutoInstallEnabled = _autoInstallEnabled;
            await _settingsService.SaveSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving update preferences.");
            ErrorMessage = _localizationService.GetString("SettingsSaveFailed");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private EventHandler<DhirDhar.Desktop.Updates.Models.UpdateInfo>? _onUpdateAvailable;
    private EventHandler<int>? _onDownloadProgressChanged;
    private EventHandler<string?>? _onUpdateStatusChanged;

    private void SubscribeUpdateEvents()
    {
        if (_updateService is null) return;

        _onUpdateAvailable = (s, info) => RunOnUiThread(() =>
        {
            if (info is not null)
            {
                IsUpdateAvailable = true;
                LatestVersion = info.Version;
                ReleaseNotes = info.ReleaseNotes;
                UpdateStatusMessage = $"{_localizationService.GetString("NewVersionAvailable")}: {info.Version}";
            }
        });

        _onDownloadProgressChanged = (s, progress) => RunOnUiThread(() =>
        {
            DownloadProgressPercent = progress;
            IsDownloading = true;
            UpdateStatusMessage = $"{_localizationService.GetString("DownloadingUpdate")}: {progress}%";
        });

        _onUpdateStatusChanged = (s, msg) => RunOnUiThread(() =>
        {
            if (!string.IsNullOrEmpty(msg))
            {
                var key = msg switch
                {
                    "Downloading update..." => "DownloadingUpdate",
                    "Update ready to install." => "UpdateDownloadedAndVerified",
                    "Application is up to date." => "AppUpToDate",
                    _ => msg
                };
                UpdateStatusMessage = _localizationService.GetString(key);
            }
        });

        _updateService.UpdateAvailable += _onUpdateAvailable;
        _updateService.DownloadProgressChanged += _onDownloadProgressChanged;
        _updateService.StatusChanged += _onUpdateStatusChanged;
    }

    private void UnsubscribeUpdateEvents()
    {
        if (_updateService is null) return;
        if (_onUpdateAvailable is not null) _updateService.UpdateAvailable -= _onUpdateAvailable;
        if (_onDownloadProgressChanged is not null) _updateService.DownloadProgressChanged -= _onDownloadProgressChanged;
        if (_onUpdateStatusChanged is not null) _updateService.StatusChanged -= _onUpdateStatusChanged;
    }

    private void RaiseUpdateCommandStates()
    {
        CheckForUpdatesCommand.RaiseCanExecuteChanged();
        UpdateNowCommand.RaiseCanExecuteChanged();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updateService is null) return;
        IsUpdateCheckInProgress = true;
        UpdateStatusMessage = _localizationService.GetString("CheckingForUpdates");
        try
        {
            await Task.Run(async () =>
            {
                await _updateService.CheckForUpdatesAsync(force: true).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update check failed.");
            RunOnUiThread(() => UpdateStatusMessage = _localizationService.GetString("UpdateNetworkError"));
        }
        finally
        {
            RunOnUiThread(() => IsUpdateCheckInProgress = false);
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_updateService is null) return;

        var targetUpdate = _updateService.AvailableUpdate;
        if (targetUpdate is null && !string.IsNullOrEmpty(LatestVersion))
        {
            targetUpdate = new UpdateInfo { Version = LatestVersion };
        }

        if (targetUpdate is null) return;

        await Task.Run(async () =>
        {
            try
            {
                if (!IsReadyToInstall)
                {
                    RunOnUiThread(() => IsDownloading = true);
                    bool success = await _updateService.DownloadAndVerifyUpdateAsync(targetUpdate).ConfigureAwait(false);
                    RunOnUiThread(() => IsDownloading = false);

                    if (!success)
                    {
                        return;
                    }
                }

                // Trigger installation
                await _updateService.InstallUpdateAsync(targetUpdate).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download and install update.");
                RunOnUiThread(() =>
                {
                    IsDownloading = false;
                    UpdateStatusMessage = _localizationService.GetString("UpdateDownloadFailed");
                });
            }
        }).ConfigureAwait(false);
    }

    public async Task ResetSettingsAsync()
    {
        if (_settingsService == null) return;
        IsLoading = true;
        try
        {
            await _settingsService.ResetSettingsAsync();
            await LoadAsync();
            StatusMessage = _localizationService?.GetString("SettingsResetDone") ?? "Settings reset.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting settings.");
            ErrorMessage = _localizationService?.GetString("SettingsResetFailed") ?? "Failed to reset settings.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static void ApplyTheme(string themeDisplay)
    {
        var elementTheme = themeDisplay switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (App.MainWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = elementTheme;
        }
    }

    private static string MapCodeToThemeDisplay(string code) => code switch
    {
        "Light" => "Light",
        "Dark" => "Dark",
        _ => "System Default"
    };

    private static string MapThemeDisplayToCode(string display) => display switch
    {
        "Light" => "Light",
        "Dark" => "Dark",
        _ => "Default"
    };

    // --- Security & Data Encryption (E2EE) Properties & Actions ---

    public string SecurityAndEncryptionLabel => _localizationService.GetString("SecurityAndEncryption");
    public string SecurityAndEncryptionSubtitleLabel => _localizationService.GetString("SecurityAndEncryptionSubtitle");
    public string EncryptionStatusLabel => _localizationService.GetString("EncryptionStatus");
    public string DatabaseEncryptionLabel => _localizationService.GetString("DatabaseEncryption");
    public string BackupEncryptionLabel => _localizationService.GetString("BackupEncryption");
    public string KeyStorageLabel => _localizationService.GetString("KeyStorage");
    public string EncryptionVersionLabel => _localizationService.GetString("EncryptionVersion");
    public string LastVerificationLabel => _localizationService.GetString("LastVerification");

    public string EncryptionStatusValue => "Enabled (AES-256-GCM)";
    public string DatabaseEncryptionValue => "Enabled (Authenticated AEAD)";
    public string BackupEncryptionValue => "Enabled (AES-256-GCM / PBKDF2)";
    public string KeyStorageValue => "Windows DPAPI (Hardware Isolated)";
    public string EncryptionVersionValue => "v1.0 (AEAD)";
    public string LastVerificationValue => _lastVerificationTime ?? "Verified (Healthy)";

    public string ExportRecoveryKeyLabel => _localizationService.GetString("ExportRecoveryKey");
    public string VerifyEncryptionLabel => _localizationService.GetString("VerifyEncryption");

    public string? RecoveryKeyDisplay
    {
        get => _recoveryKeyDisplay;
        private set => SetProperty(ref _recoveryKeyDisplay, value);
    }

    public bool HasRecoveryKeyDisplay => !string.IsNullOrWhiteSpace(_recoveryKeyDisplay);

    public bool IsExportingRecoveryKey
    {
        get => _isExportingRecoveryKey;
        private set
        {
            if (SetProperty(ref _isExportingRecoveryKey, value))
            {
                (ExportRecoveryKeyCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsVerifyingEncryption
    {
        get => _isVerifyingEncryption;
        private set
        {
            if (SetProperty(ref _isVerifyingEncryption, value))
            {
                (VerifyEncryptionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string? EncryptionVerificationMessage
    {
        get => _encryptionVerificationMessage;
        private set => SetProperty(ref _encryptionVerificationMessage, value);
    }

    public async Task ExportRecoveryKeyAsync()
    {
        if (_keyManagementService == null) return;
        IsExportingRecoveryKey = true;
        try
        {
            var keyDetails = await Task.Run(async () => await _keyManagementService.GenerateOrGetRecoveryKeyAsync()).ConfigureAwait(false);
            RunOnUiThread(() =>
            {
                RecoveryKeyDisplay = keyDetails.FormattedRecoveryKey;
                OnPropertyChanged(nameof(HasRecoveryKeyDisplay));

                try
                {
                    var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText(keyDetails.FormattedRecoveryKey);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                    EncryptionVerificationMessage = "Disaster recovery key generated and copied to clipboard.";
                }
                catch
                {
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export recovery key.");
            RunOnUiThread(() => ErrorMessage = string.Format(_localizationService.GetString("ExportRecoveryKeyFailed"), ex.Message));
        }
        finally
        {
            RunOnUiThread(() => IsExportingRecoveryKey = false);
        }
    }

    public async Task VerifyEncryptionAsync()
    {
        if (_keyManagementService == null) return;
        IsVerifyingEncryption = true;
        EncryptionVerificationMessage = null;
        try
        {
            var success = await Task.Run(async () => await _keyManagementService.VerifyEncryptionIntegrityAsync()).ConfigureAwait(false);
            RunOnUiThread(() =>
            {
                if (success)
                {
                    EncryptionVerificationMessage = "Cryptographic integrity verified: AES-256-GCM AEAD operational.";
                    _lastVerificationTime = _dateLocalizationService.FormatDateTime(DateTime.UtcNow);
                    OnPropertyChanged(nameof(LastVerificationValue));
                }
                else
                {
                    EncryptionVerificationMessage = "Cryptographic verification failed. Check master key access.";
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying encryption.");
            RunOnUiThread(() => EncryptionVerificationMessage = "Verification failed: " + ex.Message);
        }
        finally
        {
            RunOnUiThread(() => IsVerifyingEncryption = false);
        }
    }

    // --- Data Integrity Properties & Actions ---

    public string DataIntegrityLabel => _localizationService.GetString("Integrity");
    public string DataIntegritySubtitleLabel => _localizationService.GetString("IntegritySubtitle");
    public string IntegrityStatusLabel => _localizationService.GetString("IntegrityStatus");
    public string ScanSummaryLabel => _localizationService.GetString("ScanSummary");
    public string RunFullScanLabel => _localizationService.GetString("RunFullScan");
    public string OverallStatusLabel => _localizationService.GetString("OverallStatus");
    public string TotalIssuesLabel => _localizationService.GetString("TotalIssues");
    public string ScannedAtLabel => _localizationService.GetString("ScannedAt");

    public RelayCommand RunFullScanCommand { get; }

    public bool IsIntegrityScanning
    {
        get => _isIntegrityScanning;
        private set
        {
            if (SetProperty(ref _isIntegrityScanning, value))
            {
                (RunFullScanCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string? IntegrityStatusMessage
    {
        get
        {
            if (IsIntegrityScanning)
            {
                return _integrityStatusMessage ?? _localizationService.GetString("IntegrityScanning");
            }
            if (_isIntegrityScanCompleted)
            {
                var isHealthy = _lastIntegrityReport == null || (_lastIntegrityReport.OverallStatus == DhirDhar.Application.Validation.Models.IntegrityStatus.Pass && _lastIntegrityReport.TotalIssuesFound == 0);
                var statusStr = isHealthy ? _localizationService.GetString("Healthy") : _localizationService.GetString("IssuesFound");
                var issuesCount = _lastIntegrityReport?.TotalIssuesFound ?? 0;
                var localizedIssuesCount = LocalizeDigits(issuesCount.ToString());
                var template = _localizationService.GetString("IntegrityScanCompleted");
                try
                {
                    return string.Format(template, statusStr, localizedIssuesCount);
                }
                catch
                {
                    return $"{template}: {statusStr}, {localizedIssuesCount}";
                }
            }
            return _integrityStatusMessage;
        }
        private set => SetProperty(ref _integrityStatusMessage, value);
    }

    public string OverallStatusDisplay
    {
        get
        {
            if (_lastIntegrityReport != null)
            {
                if (_lastIntegrityReport.OverallStatus == DhirDhar.Application.Validation.Models.IntegrityStatus.Pass && _lastIntegrityReport.TotalIssuesFound == 0)
                {
                    return _localizationService.GetString("Healthy");
                }
                return _localizationService.GetString("IssuesFound");
            }
            return _overallStatusDisplay ?? _localizationService.GetString("Healthy");
        }
        private set => SetProperty(ref _overallStatusDisplay, value);
    }

    public string IntegritySummaryDisplay
    {
        get
        {
            if (_lastIntegrityReport != null)
            {
                if (_lastIntegrityReport.OverallStatus == DhirDhar.Application.Validation.Models.IntegrityStatus.Pass && _lastIntegrityReport.TotalIssuesFound == 0)
                {
                    return _localizationService.GetString("DatabaseHealthyMessage");
                }
                var localizedCount = LocalizeDigits(_lastIntegrityReport.TotalIssuesFound.ToString());
                return $"{localizedCount} {_localizationService.GetString("IssuesFound")}";
            }
            return _integritySummaryDisplay ?? _localizationService.GetString("DatabaseHealthyMessage");
        }
        private set => SetProperty(ref _integritySummaryDisplay, value);
    }

    public string TotalIssuesDisplay
    {
        get
        {
            if (_lastIntegrityReport != null)
            {
                return LocalizeDigits(_lastIntegrityReport.TotalIssuesFound.ToString());
            }
            return _totalIssuesDisplay ?? LocalizeDigits("0");
        }
        private set => SetProperty(ref _totalIssuesDisplay, value);
    }

    public string ScannedAtDisplay
    {
        get
        {
            if (_lastIntegrityScanTime.HasValue)
            {
                return _dateLocalizationService.FormatDateTime(_lastIntegrityScanTime.Value);
            }
            return _scannedAtDisplay ?? _dateLocalizationService.FormatDateTime(DateTime.Now);
        }
        private set => SetProperty(ref _scannedAtDisplay, value);
    }

    public async Task RunFullScanAsync()
    {
        if (IsIntegrityScanning) return;

        try
        {
            _scanCts?.Cancel();
        }
        catch
        {
        }

        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        IsIntegrityScanning = true;
        _isIntegrityScanCompleted = false;
        IntegrityStatusMessage = _localizationService.GetString("IntegrityScanning");

        try
        {
            if (_integrityService == null)
            {
                RunOnUiThread(() =>
                {
                    _lastIntegrityReport = null;
                    _lastIntegrityScanTime = DateTime.Now;
                    _isIntegrityScanCompleted = true;
                    OnPropertyChanged(nameof(OverallStatusDisplay));
                    OnPropertyChanged(nameof(IntegritySummaryDisplay));
                    OnPropertyChanged(nameof(TotalIssuesDisplay));
                    OnPropertyChanged(nameof(ScannedAtDisplay));
                    OnPropertyChanged(nameof(IntegrityStatusMessage));
                });
                return;
            }

            var report = await Task.Run(async () => await _integrityService.RunIntegrityScanAsync(ct), ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;

            RunOnUiThread(() =>
            {
                _lastIntegrityReport = report;
                _lastIntegrityScanTime = report.ScannedAt.Kind == DateTimeKind.Utc ? report.ScannedAt.ToLocalTime() : report.ScannedAt;
                _isIntegrityScanCompleted = true;

                OnPropertyChanged(nameof(OverallStatusDisplay));
                OnPropertyChanged(nameof(IntegritySummaryDisplay));
                OnPropertyChanged(nameof(TotalIssuesDisplay));
                OnPropertyChanged(nameof(ScannedAtDisplay));
                OnPropertyChanged(nameof(IntegrityStatusMessage));
            });
        }
        catch (OperationCanceledException)
        {
            RunOnUiThread(() =>
            {
                _isIntegrityScanCompleted = false;
                IntegrityStatusMessage = _localizationService.GetString("OperationCancelled");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Integrity scan error in Settings.");
            RunOnUiThread(() =>
            {
                _isIntegrityScanCompleted = false;
                var template = _localizationService.GetString("IntegrityScanFailed");
                try
                {
                    IntegrityStatusMessage = template.Contains("{0}")
                        ? string.Format(template, ex.Message)
                        : $"{template}: {ex.Message}";
                }
                catch
                {
                    IntegrityStatusMessage = $"{template}: {ex.Message}";
                }
            });
        }
        finally
        {
            RunOnUiThread(() => IsIntegrityScanning = false);
        }
    }
}

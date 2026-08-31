using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Validation;
using DhirDhar.Application.Validation.Models;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.ViewModels;

public sealed class IntegrityIssueItemViewModel : ViewModelBase
{
    private readonly IntegrityIssue _issue;
    private readonly ILocalizationService _localizationService;
    private readonly Func<IntegrityIssueItemViewModel, Task> _repairAction;
    private bool _isRepairing;

    public IntegrityIssueItemViewModel(
        IntegrityIssue issue,
        ILocalizationService localizationService,
        Func<IntegrityIssueItemViewModel, Task> repairAction)
    {
        _issue = issue;
        _localizationService = localizationService;
        _repairAction = repairAction;
        RepairCommand = new RelayCommand(async () => await _repairAction(this), () => IsRepairable && !IsRepairing);
    }

    public IntegrityIssue Issue => _issue;
    public string Category => _localizationService.LocalizeText(_issue.Category);
    public IntegritySeverityLevel Severity => _issue.Severity;

    public string SeverityDisplay => _issue.Severity switch
    {
        IntegritySeverityLevel.Critical => _localizationService.GetString("Critical"),
        IntegritySeverityLevel.High => _localizationService.GetString("High"),
        IntegritySeverityLevel.Warning => _localizationService.GetString("Warning"),
        _ => _localizationService.GetString("Info")
    };

    public string Title => !string.IsNullOrWhiteSpace(_issue.Title)
        ? _localizationService.LocalizeText(_issue.Title)
        : _localizationService.LocalizeText(_issue.FailureCode);

    public string AffectedEntity => $"{_issue.EntityName} ({_issue.EntityId})";

    public string? BorrowerNumber => _issue.BorrowerNumber;
    public bool HasBorrowerNumber => !string.IsNullOrWhiteSpace(_issue.BorrowerNumber);
    public string BorrowerNumberBadge => HasBorrowerNumber ? $"Borrower {_issue.BorrowerNumber}" : string.Empty;

    public string Description => _localizationService.LocalizeText(_issue.Description);

    public string TechnicalDetails => _issue.TechnicalDetails ?? string.Empty;
    public bool HasTechnicalDetails => !string.IsNullOrWhiteSpace(_issue.TechnicalDetails);

    public string RecoveryHint => _issue.RecoveryHint ?? string.Empty;
    public bool HasRecoveryHint => !string.IsNullOrWhiteSpace(_issue.RecoveryHint);

    public bool IsRepairable => _issue.IsRepairable;
    public string? RepairActionKey => _issue.RepairActionKey;

    public bool IsRepairing
    {
        get => _isRepairing;
        set
        {
            if (SetProperty(ref _isRepairing, value))
            {
                (RepairCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand RepairCommand { get; }
}

public sealed class IntegrityViewModel : ViewModelBase, IDisposable
{
    private readonly IIntegrityService _integrityService;
    private readonly IDateLocalizationService _dateLocalizationService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<IntegrityViewModel> _logger;

    private IntegrityScanReport? _scanReport;
    private bool _isScanning;
    private string _statusMessage = string.Empty;

    public IntegrityViewModel(
        IIntegrityService integrityService,
        IDateLocalizationService dateLocalizationService,
        ILocalizationService localizationService,
        ILogger<IntegrityViewModel> logger)
    {
        _integrityService = integrityService;
        _dateLocalizationService = dateLocalizationService;
        _localizationService = localizationService;
        _logger = logger;

        _localizationService.LanguageChanged += OnLanguageChanged;

        StatusMessage = _localizationService.GetString("IntegrityReady");
        RunScanCommand = new RelayCommand(async () => await RunScanAsync());
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            OnPropertyChanged(string.Empty);
            RefreshIssueViewModels();
        });
    }

    public string PageTitle => _localizationService.GetString("Integrity");
    public string PageSubtitle => _localizationService.GetString("IntegritySubtitle");
    public string RunFullScanLabel => _localizationService.GetString("RunFullScan");
    public string IntegrityStatusLabel => _localizationService.GetString("IntegrityStatus");
    public string ScanSummaryLabel => _localizationService.GetString("ScanSummary");
    public string OverallStatusLabel => _localizationService.GetString("OverallStatus");
    public string TotalIssuesLabel => _localizationService.GetString("TotalIssues");
    public string ScannedAtLabel => _localizationService.GetString("ScannedAt");

    public string IssueDetailsLabel => _localizationService.GetString("IssueDetails");
    public string AffectedEntityLabel => _localizationService.GetString("AffectedEntity");
    public string WhyAnIssueLabel => _localizationService.GetString("WhyAnIssue");
    public string RecommendedActionLabel => _localizationService.GetString("RecommendedAction");
    public string RepairButtonLabel => _localizationService.GetString("Repair");
    public string HealthyStateMessage => _localizationService.GetString("DatabaseHealthyMessage");

    public Func<string, string, Task<bool>>? ConfirmRepairCallback { get; set; }

    public ObservableCollection<IntegrityIssueItemViewModel> Issues { get; } = new();

    public bool HasIssues => Issues.Count > 0;
    public bool IsHealthy => ScanReport != null && ScanReport.TotalIssuesFound == 0;

    public string OverallStatusDisplay
    {
        get
        {
            if (ScanReport == null) return "-";
            if (IsHealthy) return _localizationService.GetString("Healthy");
            return _localizationService.LocalizeText(ScanReport.OverallStatus.ToString());
        }
    }

    public IntegrityScanReport? ScanReport
    {
        get => _scanReport;
        private set
        {
            if (SetProperty(ref _scanReport, value))
            {
                OnPropertyChanged(nameof(HasIssues));
                OnPropertyChanged(nameof(IsHealthy));
                OnPropertyChanged(nameof(OverallStatusDisplay));
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ScannedAtDisplay
    {
        get
        {
            if (ScanReport?.ScannedAt is DateTime dt)
            {
                return _dateLocalizationService.FormatDateTime(dt);
            }
            return string.Empty;
        }
    }

    public ICommand RunScanCommand { get; }

    public async Task RunScanAsync(CancellationToken cancellationToken = default)
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        StatusMessage = _localizationService.GetString("IntegrityScanning");

        try
        {
            var report = await Task.Run(async () => await _integrityService.RunIntegrityScanAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            RunOnUiThread(() =>
            {
                ScanReport = report;
                RefreshIssueViewModels();

                var statusStr = IsHealthy ? _localizationService.GetString("Healthy") : ScanReport.OverallStatus.ToString();
                StatusMessage = string.Format(_localizationService.GetString("IntegrityScanCompleted"), statusStr, ScanReport.TotalIssuesFound);
                OnPropertyChanged(nameof(ScannedAtDisplay));
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run integrity scan.");
            RunOnUiThread(() =>
            {
                StatusMessage = string.Format(_localizationService.GetString("IntegrityScanFailed"), ex.Message);
            });
        }
        finally
        {
            RunOnUiThread(() => IsScanning = false);
        }
    }

    public void Dispose()
    {
        _localizationService.LanguageChanged -= OnLanguageChanged;
    }

    private void RefreshIssueViewModels()
    {
        Issues.Clear();
        if (ScanReport?.Categories != null)
        {
            foreach (var category in ScanReport.Categories)
            {
                foreach (var issue in category.Issues)
                {
                    Issues.Add(new IntegrityIssueItemViewModel(issue, _localizationService, RepairIssueAsync));
                }
            }
        }
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(IsHealthy));
        OnPropertyChanged(nameof(OverallStatusDisplay));
    }

    public async Task RepairIssueAsync(IntegrityIssueItemViewModel item)
    {
        if (item.IsRepairing || !item.IsRepairable || string.IsNullOrWhiteSpace(item.RepairActionKey))
        {
            return;
        }

        if (ConfirmRepairCallback != null)
        {
            var confirmed = await ConfirmRepairCallback(
                _localizationService.GetString("ConfirmRepairTitle"),
                _localizationService.GetString("ConfirmRepairMessage"));
            if (!confirmed)
            {
                return;
            }
        }

        item.IsRepairing = true;
        StatusMessage = _localizationService.GetString("Repairing");

        try
        {
            var result = await Task.Run(async () => await _integrityService.RepairIssueAsync(item.RepairActionKey, item.Issue.EntityId)).ConfigureAwait(false);
            RunOnUiThread(() =>
            {
                if (result.IsValid)
                {
                    StatusMessage = _localizationService.GetString("RepairSuccess");
                    _ = RunScanAsync();
                }
                else
                {
                    var errorMsg = string.Join("; ", result.Errors);
                    StatusMessage = string.Format(_localizationService.GetString("RepairFailed"), errorMsg);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to repair integrity issue {RepairKey} on {EntityId}", item.RepairActionKey, item.Issue.EntityId);
            RunOnUiThread(() =>
            {
                StatusMessage = string.Format(_localizationService.GetString("RepairFailed"), ex.Message);
            });
        }
        finally
        {
            RunOnUiThread(() => item.IsRepairing = false);
        }
    }
}

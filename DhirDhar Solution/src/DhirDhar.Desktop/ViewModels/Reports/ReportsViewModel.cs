using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Reports;
using DhirDhar.Application.Reports.Models;
using DhirDhar.Desktop.ViewModels;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.ViewModels.Reports;

public sealed record ReportTypeOption(string Value, string Label);

public sealed record TransactionTypeFilterOption(string Value, string Label);

public sealed class ReportsViewModel : ViewModelBase
{
    private readonly IReportService _reportService;
    private readonly IBorrowerService _borrowerService;
    private readonly IPdfExportService? _pdfExportService;
    private readonly ITranslationService _translationService;
    private readonly ILogger<ReportsViewModel> _logger;
    private readonly DhirDhar.Application.Localization.ILocalizationService _localizationService;

    private ObservableCollection<BorrowerSummary> _borrowers = new();
    private ObservableCollection<BorrowerSummary> _searchResults = new();
    private BorrowerSummary? _selectedBorrower;
    private string _searchQueryText = string.Empty;
    private DateTimeOffset? _fromDate;
    private DateTimeOffset? _toDate = DateTimeOffset.Now.Date;
    private string _transactionTypeFilter = "All";
    private string _selectedReportType = "BorrowerStatement";
    private bool _isGenerating;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private object? _currentReport;

    public ReportsViewModel(
        IReportService reportService,
        IBorrowerService borrowerService,
        DhirDhar.Application.Localization.ILocalizationService localizationService,
        ITranslationService translationService,
        ILogger<ReportsViewModel> logger,
        IPdfExportService? pdfExportService = null)
    {
        _reportService = reportService;
        _borrowerService = borrowerService;
        _localizationService = localizationService;
        _translationService = translationService;
        _logger = logger;
        _pdfExportService = pdfExportService ?? App.ServiceProvider?.GetService(typeof(IPdfExportService)) as IPdfExportService;

        _localizationService.LanguageChanged += (s, e) =>
        {
            OnPropertyChanged(string.Empty);
            OnPropertyChanged(nameof(ReportTypeOptions));
            OnPropertyChanged(nameof(TransactionTypeFilterOptions));
            _ = LoadBorrowersAsync();
            if (HasReport && CanGenerate)
            {
                _ = GenerateReportAsync();
            }
        };

        GenerateCommand = new RelayCommand(async () => await GenerateReportAsync(), () => CanGenerate);
        RefreshCommand = new RelayCommand(async () => await LoadBorrowersAsync());
        RetryCommand = new RelayCommand(async () => await GenerateReportAsync(), () => CanGenerate);
        ExportPdfCommand = new RelayCommand(async () => await ExportPdfAsync(), () => CanExport);
        ClearBorrowerCommand = new RelayCommand(ClearBorrowerSelection);
    }

    public ObservableCollection<BorrowerSummary> Borrowers
    {
        get => _borrowers;
        private set => SetProperty(ref _borrowers, value);
    }

    public ObservableCollection<BorrowerSummary> SearchResults
    {
        get => _searchResults;
        private set => SetProperty(ref _searchResults, value);
    }

    public string SearchQueryText
    {
        get => _searchQueryText;
        set
        {
            if (SetProperty(ref _searchQueryText, value))
            {
                if (string.IsNullOrWhiteSpace(value) && _selectedBorrower is not null)
                {
                    SelectedBorrower = null;
                }
            }
        }
    }

    public BorrowerSummary? SelectedBorrower
    {
        get => _selectedBorrower;
        set
        {
            var oldBorrowerId = _selectedBorrower?.Id;
            if (SetProperty(ref _selectedBorrower, value))
            {
                OnPropertyChanged(nameof(HasSelectedBorrower));
                OnPropertyChanged(nameof(CanGenerate));
                GenerateCommand?.RaiseCanExecuteChanged();
                RetryCommand?.RaiseCanExecuteChanged();

                if (value != null && value.Id != oldBorrowerId)
                {
                    _ = UpdateFromDateForBorrowerAsync(value);
                }
                else if (value is null)
                {
                    FromDate = null;
                }
            }
        }
    }

    public async Task UpdateFromDateForBorrowerAsync(BorrowerSummary borrower)
    {
        try
        {
            var dbBorrower = await _borrowerService.GetByIdAsync(borrower.Id).ConfigureAwait(false);
            DateTime originalDate;
            if (dbBorrower != null)
            {
                if (dbBorrower.LoanDate.HasValue && dbBorrower.LoanDate.Value != default)
                {
                    originalDate = (dbBorrower.EntryDate != default && dbBorrower.EntryDate < dbBorrower.LoanDate.Value)
                        ? dbBorrower.EntryDate
                        : dbBorrower.LoanDate.Value;
                }
                else
                {
                    originalDate = dbBorrower.EntryDate != default ? dbBorrower.EntryDate : DateTime.Today;
                }
            }
            else
            {
                if (borrower.LoanDate.HasValue && borrower.LoanDate.Value != default)
                {
                    originalDate = (borrower.EntryDate != default && borrower.EntryDate < borrower.LoanDate.Value)
                        ? borrower.EntryDate
                        : borrower.LoanDate.Value;
                }
                else
                {
                    originalDate = borrower.EntryDate != default ? borrower.EntryDate : DateTime.Today;
                }
            }

            FromDate = new DateTimeOffset(originalDate.Date);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch borrower from database for FromDate update.");
            var fallbackDate = borrower.LoanDate ?? (borrower.EntryDate != default ? borrower.EntryDate : DateTime.Today);
            FromDate = new DateTimeOffset(fallbackDate.Date);
        }
    }

    public bool HasSelectedBorrower => SelectedBorrower is not null;

    public DateTimeOffset? FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value))
            {
                OnPropertyChanged(nameof(CanGenerate));
                GenerateCommand?.RaiseCanExecuteChanged();
                RetryCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public DateTimeOffset? ToDate
    {
        get => _toDate;
        set
        {
            if (SetProperty(ref _toDate, value))
            {
                OnPropertyChanged(nameof(CanGenerate));
                GenerateCommand?.RaiseCanExecuteChanged();
                RetryCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string TransactionTypeFilter
    {
        get => _transactionTypeFilter;
        set => SetProperty(ref _transactionTypeFilter, value);
    }

    public string SelectedReportType
    {
        get => _selectedReportType;
        set
        {
            if (SetProperty(ref _selectedReportType, value))
            {
                OnPropertyChanged(nameof(CanGenerate));
                OnPropertyChanged(nameof(CanExport));
                OnPropertyChanged(nameof(SelectedReportTypeLabel));
                GenerateCommand?.RaiseCanExecuteChanged();
                RetryCommand?.RaiseCanExecuteChanged();
                ExportPdfCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsGenerating
    {
        get => _isGenerating;
        private set
        {
            if (SetProperty(ref _isGenerating, value))
            {
                OnPropertyChanged(nameof(CanGenerate));
                OnPropertyChanged(nameof(CanExport));
                GenerateCommand?.RaiseCanExecuteChanged();
                RetryCommand?.RaiseCanExecuteChanged();
                ExportPdfCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string PageTitle => _localizationService.GetString("Reports");
    public string PageSubtitle => _localizationService.GetString("ReportsSubtitle");
    public string RefreshText => _localizationService.GetString("Refresh");
    public string GenerateReportText => _localizationService.GetString("GenerateReport");
    public string RetryText => _localizationService.GetString("Retry");
    public string ReportTypeLabel => _localizationService.GetString("ReportType");
    public string BorrowerLabel => _localizationService.GetString("Borrower");
    public string FromLabel => _localizationService.GetString("From");
    public string ToLabel => _localizationService.GetString("To");
    public string TransactionTypeLabel => _localizationService.GetString("Type");
    public string ExportPdfLabel => _localizationService.GetString("ExportPdf");
    public string GeneratingReportLabel => _localizationService.GetString("GeneratingReport");
    public string SelectedReportTypeLabel => _localizationService.GetString(SelectedReportType);
    public string SearchBorrowerPlaceholder => _localizationService.GetString("SearchBorrowerPlaceholder");
    public string ClearText => _localizationService.GetString("Clear");

    public ObservableCollection<ReportTypeOption> ReportTypeOptions => new()
    {
        new("BorrowerStatement", _localizationService.GetString("BorrowerStatement")),
        new("TransactionReport", _localizationService.GetString("TransactionReport")),
        new("InterestReport", _localizationService.GetString("InterestReport")),
        new("OutstandingReport", _localizationService.GetString("OutstandingReport")),
        new("BorrowerSummary", _localizationService.GetString("BorrowerSummary"))
    };

    public ObservableCollection<TransactionTypeFilterOption> TransactionTypeFilterOptions => new()
    {
        new("All", _localizationService.GetString("All")),
        new("Deposit", _localizationService.GetString("Deposit")),
        new("Withdrawal", _localizationService.GetString("Withdrawal"))
    };

    public object? CurrentReport
    {
        get => _currentReport;
        private set
        {
            if (SetProperty(ref _currentReport, value))
            {
                OnPropertyChanged(nameof(HasReport));
                OnPropertyChanged(nameof(CanExport));
                ExportPdfCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasReport => CurrentReport != null;

    public bool CanGenerate =>
        !IsGenerating &&
        FromDate.HasValue &&
        ToDate.HasValue &&
        FromDate.Value.Date <= ToDate.Value.Date &&
        (SelectedReportType != "BorrowerStatement" || SelectedBorrower is not null);

    public bool CanExport => HasReport && !IsGenerating;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public RelayCommand GenerateCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand ExportPdfCommand { get; }
    public RelayCommand ClearBorrowerCommand { get; }

    public void SearchBorrowers(string query)
    {
        SearchQueryText = query;
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResults = new ObservableCollection<BorrowerSummary>(_borrowers);
            return;
        }

        var rawQ = query.Trim();
        if (rawQ.StartsWith("DHIRDHAR|ACCOUNT|", StringComparison.OrdinalIgnoreCase))
        {
            rawQ = rawQ.Substring("DHIRDHAR|ACCOUNT|".Length).Trim();
        }
        else if (rawQ.StartsWith("DHIRDHAR|", StringComparison.OrdinalIgnoreCase))
        {
            var parts = rawQ.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 3) rawQ = parts[2];
        }
        var cleanQ = rawQ.TrimStart('#').Trim();
        var qLower = cleanQ.ToLowerInvariant();
        var qAscii = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(qLower);
        var qEnglish = ScriptTranslator.ToEnglish(cleanQ).Trim();
        var qGujarati = ScriptTranslator.ToGujarati(cleanQ).Trim();
        var qHindi = ScriptTranslator.ToHindi(cleanQ).Trim();

        var matches = _borrowers.Where(b =>
            MatchesTerm(b.FullName, qLower, qEnglish, qGujarati, qHindi) ||
            MatchesTerm(b.Name, qLower, qEnglish, qGujarati, qHindi) ||
            MatchesTerm(b.BorrowerNumber, qLower, qAscii, qEnglish, qGujarati, qHindi) ||
            MatchesTerm(b.Contact, qLower, qAscii, qEnglish) ||
            MatchesTerm(b.FatherName, qLower, qEnglish, qGujarati, qHindi) ||
            MatchesTerm(b.Surname, qLower, qEnglish, qGujarati, qHindi) ||
            MatchesTerm(b.Village, qLower, qEnglish, qGujarati, qHindi) ||
            MatchesTerm(b.AadharNumber, qLower, qAscii, qEnglish)
        ).ToList();

        SearchResults = new ObservableCollection<BorrowerSummary>(matches);

        var exactMatch = matches.FirstOrDefault(b =>
            string.Equals(b.BorrowerNumber, cleanQ, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b.FullName, cleanQ, StringComparison.OrdinalIgnoreCase) ||
            string.Equals($"{b.FullName} ({b.BorrowerNumber})", cleanQ, StringComparison.OrdinalIgnoreCase));

        if (exactMatch is not null && SelectedBorrower?.Id != exactMatch.Id)
        {
            SelectBorrower(exactMatch);
        }
    }

    private static bool MatchesTerm(string? target, params string[] searchTerms)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        var targetLower = target.ToLowerInvariant();
        var targetAscii = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(targetLower);

        foreach (var term in searchTerms)
        {
            if (!string.IsNullOrWhiteSpace(term))
            {
                var termLower = term.ToLowerInvariant();
                if (targetLower.Contains(termLower, StringComparison.OrdinalIgnoreCase) ||
                    targetAscii.Contains(termLower, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void SelectBorrower(BorrowerSummary? borrower)
    {
        SelectedBorrower = borrower;
        if (borrower is not null)
        {
            _searchQueryText = $"{borrower.FullName} ({borrower.BorrowerNumber})";
            OnPropertyChanged(nameof(SearchQueryText));
        }
    }

    public void ClearBorrowerSelection()
    {
        SelectedBorrower = null;
        SearchQueryText = string.Empty;
        SearchResults = new ObservableCollection<BorrowerSummary>(_borrowers);
        FromDate = null;
        ToDate = DateTimeOffset.Now.Date;
    }

    public async Task LoadBorrowersAsync()
    {
        try
        {
            var result = await _borrowerService.GetListAsync(BorrowerFilter.All, null, 1, 0).ConfigureAwait(false);
            var currentLang = _localizationService.CurrentLanguage;
            var items = result.Items.Localize(_translationService, currentLang).ToList();

            Borrowers = new ObservableCollection<BorrowerSummary>(items);
            SearchResults = new ObservableCollection<BorrowerSummary>(items);

            if (SelectedBorrower != null)
            {
                var refreshedSelected = items.FirstOrDefault(b => b.Id == SelectedBorrower.Id);
                if (refreshedSelected != null)
                {
                    SelectedBorrower = refreshedSelected;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load borrowers for reports.");
        }
    }

    private async Task GenerateReportAsync()
    {
        if (IsGenerating) return;
        var fromDt = FromDate?.DateTime ?? (SelectedBorrower != null ? (SelectedBorrower.LoanDate ?? (SelectedBorrower.EntryDate != default ? SelectedBorrower.EntryDate : DateTime.Today)) : DateTime.Today);
        var toDt = ToDate?.DateTime ?? DateTime.Today;

        if (fromDt > toDt)
        {
            HasError = true;
            ErrorMessage = _localizationService.GetString("InvalidDateRange");
            StatusMessage = ErrorMessage;
            return;
        }

        IsGenerating = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusMessage = _localizationService.GetString("GeneratingReport");

        try
        {
            switch (SelectedReportType)
            {
                case "BorrowerStatement":
                    if (SelectedBorrower is null)
                    {
                        HasError = true;
                        ErrorMessage = _localizationService.GetString("SelectBorrowerRequired");
                        StatusMessage = ErrorMessage;
                        return;
                    }
                    CurrentReport = await _reportService.GenerateBorrowerStatementAsync(SelectedBorrower.Id, fromDt, toDt).ConfigureAwait(false);
                    break;

                case "TransactionReport":
                    CurrentReport = await _reportService.GenerateTransactionReportAsync(fromDt, toDt, SelectedBorrower?.Id, TransactionTypeFilter).ConfigureAwait(false);
                    break;

                case "InterestReport":
                    CurrentReport = await _reportService.GenerateInterestReportAsync(SelectedBorrower?.Id, fromDt, toDt).ConfigureAwait(false);
                    break;

                case "OutstandingReport":
                    CurrentReport = await _reportService.GenerateOutstandingReportAsync(SelectedBorrower?.Id).ConfigureAwait(false);
                    break;

                case "BorrowerSummary":
                    CurrentReport = await _reportService.GenerateBorrowerSummaryAsync(SelectedBorrower?.Id).ConfigureAwait(false);
                    break;
            }

            StatusMessage = _localizationService.GetString("ReportGeneratedSuccess");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate report '{ReportType}'.", SelectedReportType);
            HasError = true;
            ErrorMessage = $"{_localizationService.GetString("ReportGenerationFailed")} ({ex.Message})";
            StatusMessage = ErrorMessage;
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private async Task ExportPdfAsync()
    {
        if (CurrentReport is null || _pdfExportService is null) return;

        try
        {
            StatusMessage = _localizationService.GetString("ExportingPdf");
            var filePath = await _pdfExportService.ExportReportToPdfAsync(CurrentReport, SelectedReportType).ConfigureAwait(false);
            StatusMessage = $"{_localizationService.GetString("Success")}: {System.IO.Path.GetFileName(filePath)}";

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception launchEx)
            {
                _logger.LogWarning(launchEx, "PDF generated successfully at {FilePath}, but default PDF viewer could not be opened.", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export PDF for report type '{ReportType}'.", SelectedReportType);
            HasError = true;
            ErrorMessage = $"Failed to export PDF: {ex.Message}";
            StatusMessage = ErrorMessage;
        }
    }
}

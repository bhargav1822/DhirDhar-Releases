using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using DhirDhar.Application.Ledger;
using DhirDhar.Application.Ledger.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Desktop.ViewModels;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.ViewModels.Ledger;

public sealed record EventTypeOption(string Value, string Label);

public sealed class LedgerViewModel : ViewModelBase
{
    private readonly ILedgerService _ledgerService;
    private readonly DhirDhar.Application.Localization.ILocalizationService _localizationService;
    private readonly ITranslationService _translationService;
    private readonly ILogger<LedgerViewModel> _logger;

    private Guid _borrowerId;
    private LedgerSummary? _summary;
    private ObservableCollection<LedgerEntryDto> _entries = new();
    private DateTimeOffset? _startDate;
    private DateTimeOffset? _endDate = DateTimeOffset.Now.Date;
    private string _eventTypeFilter = "All";
    private string _searchTerm = string.Empty;
    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage = string.Empty;

    public LedgerViewModel(
        ILedgerService ledgerService,
        DhirDhar.Application.Localization.ILocalizationService localizationService,
        ITranslationService translationService,
        ILogger<LedgerViewModel> logger)
    {
        _ledgerService = ledgerService;
        _localizationService = localizationService;
        _translationService = translationService;
        _logger = logger;

        _localizationService.LanguageChanged += (s, e) =>
        {
            OnPropertyChanged(string.Empty);
            _ = LoadAsync();
        };

        LoadCommand = new RelayCommand(async () => await LoadAsync());
        RefreshCommand = new RelayCommand(async () => await LoadAsync());
        RetryCommand = new RelayCommand(async () => await LoadAsync());
        ClearFiltersCommand = new RelayCommand(ClearFilters);
    }

    public Guid BorrowerId
    {
        get => _borrowerId;
        set => SetProperty(ref _borrowerId, value);
    }

    public LedgerSummary? Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public ObservableCollection<LedgerEntryDto> Entries
    {
        get => _entries;
        private set => SetProperty(ref _entries, value);
    }

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchDebounceCts;

    public DateTimeOffset? StartDate
    {
        get => _startDate;
        set
        {
            if (SetProperty(ref _startDate, value))
            {
                TriggerDebouncedLoad(false);
            }
        }
    }

    public DateTimeOffset? EndDate
    {
        get => _endDate;
        set
        {
            if (SetProperty(ref _endDate, value))
            {
                TriggerDebouncedLoad(false);
            }
        }
    }

    public string EventTypeFilter
    {
        get => _eventTypeFilter;
        set
        {
            if (SetProperty(ref _eventTypeFilter, value))
            {
                TriggerDebouncedLoad(false);
            }
        }
    }

    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (SetProperty(ref _searchTerm, value))
            {
                TriggerDebouncedLoad(true);
            }
        }
    }

    private void TriggerDebouncedLoad(bool isSearchText)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var ct = _searchDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (isSearchText && !string.IsNullOrWhiteSpace(SearchTerm))
                {
                    await Task.Delay(150, ct);
                }
                if (ct.IsCancellationRequested) return;
                await LoadAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Superseded
            }
        });
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
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

    public string PageTitle => _localizationService.GetString("Ledger");
    public string PageSubtitle => _localizationService.GetString("LedgerSubtitle");
    public string RefreshText => _localizationService.GetString("Refresh");
    public string OpeningBalanceLabel => _localizationService.GetString("CurrentBalance");
    public string TotalDebitsLabel => _localizationService.GetString("TotalWithdrawals");
    public string TotalCreditsLabel => _localizationService.GetString("TotalDeposits");
    public string ClosingBalanceLabel => _localizationService.GetString("TotalOutstanding");
    public string RetryText => _localizationService.GetString("Retry");
    public string StartDateLabel => _localizationService.GetString("StartDate");
    public string EndDateLabel => _localizationService.GetString("EndDate");
    public string EventTypeLabel => _localizationService.GetString("EventType");
    public string SearchPlaceholderLabel => _localizationService.GetString("SearchEllipsis");
    public string ClearLabel => _localizationService.GetString("Clear");
    public string DateHeaderLabel => _localizationService.GetString("Date");
    public string TypeHeaderLabel => _localizationService.GetString("Type");
    public string DescriptionHeaderLabel => _localizationService.GetString("Description");
    public string AmountHeaderLabel => _localizationService.GetString("Amount");
    public string InterestHeaderLabel => _localizationService.GetString("Interest");
    public string RateHeaderLabel => _localizationService.GetString("Rate");
    public string OpeningHeaderLabel => _localizationService.GetString("Opening");
    public string ClosingHeaderLabel => _localizationService.GetString("Closing");
    public string RefHeaderLabel => _localizationService.GetString("Ref");
    public string AccountClosedLabel => _localizationService.GetString("AccountClosed");
    public string ClosedDateLabel => _localizationService.GetString("ClosedDate");
    public string NoInterestAfterClosedLabel => _localizationService.GetString("NoInterestAfterClosed");

    public ObservableCollection<EventTypeOption> EventTypeOptions => new()
    {
        new("All", _localizationService.GetString("All")),
        new("Deposit", _localizationService.GetString("Deposit")),
        new("Withdrawal", _localizationService.GetString("Withdrawal")),
        new("Interest", _localizationService.GetString("Interest"))
    };

    public string FormattedOpeningBalance => Summary is not null ? LCur(Summary.OpeningBalance) : LCur(0m);
    public string FormattedTotalDeposits => Summary is not null ? LCur(Summary.TotalDeposits) : LCur(0m);
    public string FormattedTotalWithdrawals => Summary is not null ? LCur(Summary.TotalWithdrawals) : LCur(0m);
    public string FormattedTotalInterest => Summary is not null ? LCur(Summary.TotalInterest) : LCur(0m);
    public string FormattedCurrentOutstanding => Summary is not null ? LCur(Summary.CurrentOutstanding) : LCur(0m);
    public string FormattedClosedDate => LDate(Summary?.ClosedDate, "dd-MMM-yyyy");
    public bool IsClosed => Summary?.AccountStatus == "Archived" || Summary?.AccountStatus == "Closed";

    public RelayCommand LoadCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }

    public async Task LoadAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        await LoadAsync(_loadCts.Token);
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        _logger.LogInformation("Ledger load started. Initial BorrowerId='{BorrowerId}', StartDate='{StartDate}', EndDate='{EndDate}', Filter='{Filter}', Term='{Term}'.",
            BorrowerId, StartDate, EndDate, EventTypeFilter, SearchTerm);

        try
        {
            var summary = await _ledgerService.GetSummaryAsync(BorrowerId, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) return;

            if (summary != null)
            {
                var localizedBorrowerName = _translationService.Translate(summary.BorrowerName, _localizationService.CurrentLanguage);
                Summary = new LedgerSummary(
                    summary.BorrowerId,
                    localizedBorrowerName,
                    summary.OpeningBalance,
                    summary.TotalDeposits,
                    summary.TotalWithdrawals,
                    summary.TotalInterest,
                    summary.CurrentOutstanding,
                    summary.AccountStatus,
                    summary.ClosedDate);
            }
            else
            {
                Summary = null;
            }

            if (summary != null && summary.BorrowerId != Guid.Empty)
            {
                BorrowerId = summary.BorrowerId;
            }

            var entries = await _ledgerService.GetEntriesAsync(BorrowerId, StartDate?.DateTime, EndDate?.DateTime, EventTypeFilter, SearchTerm, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) return;

            var translatedEntries = System.Linq.Enumerable.Select(entries, e => new LedgerEntryDto(
                e.Date,
                e.EventType,
                _localizationService.LocalizeText(e.Description, _localizationService.CurrentLanguage),
                e.TransactionAmount,
                e.InterestAmount,
                e.ApplicableRate,
                e.OpeningPrincipal,
                e.ClosingPrincipal,
                e.Reference,
                e.Status));

            Entries = new ObservableCollection<LedgerEntryDto>(translatedEntries);

            OnPropertyChanged(nameof(FormattedOpeningBalance));
            OnPropertyChanged(nameof(FormattedTotalDeposits));
            OnPropertyChanged(nameof(FormattedTotalWithdrawals));
            OnPropertyChanged(nameof(FormattedTotalInterest));
            OnPropertyChanged(nameof(FormattedCurrentOutstanding));
            OnPropertyChanged(nameof(FormattedClosedDate));
            OnPropertyChanged(nameof(IsClosed));

            _logger.LogInformation("Ledger load succeeded for BorrowerId='{BorrowerId}'. Entries count={Count}.", BorrowerId, entries.Count);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ledger for borrower ID='{BorrowerId}'. Exception: {Type} - {Message}", BorrowerId, ex.GetType().Name, ex.Message);
            HasError = true;
            ErrorMessage = string.Format(_localizationService.GetString("LedgerLoadFailed"), ex.Message);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private void ClearFilters()
    {
        _loadCts?.Cancel();
        _searchDebounceCts?.Cancel();
        _startDate = null;
        _endDate = DateTimeOffset.Now.Date;
        _eventTypeFilter = "All";
        _searchTerm = string.Empty;
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(EndDate));
        OnPropertyChanged(nameof(EventTypeFilter));
        OnPropertyChanged(nameof(SearchTerm));
        _ = LoadAsync();
    }
}

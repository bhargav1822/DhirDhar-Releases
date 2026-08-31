using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Interest;
using DhirDhar.Application.Localization;
using DhirDhar.Domain.Interest;
using DhirDhar.Desktop.ViewModels;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.ViewModels.Interest;

public sealed class InterestViewModel : ViewModelBase
{
    private readonly IInterestCalculationService _interestService;
    private readonly IBorrowerService _borrowerService;
    private readonly DhirDhar.Application.Localization.ILocalizationService _localizationService;
    private readonly ITranslationService _translationService;
    private readonly ILogger<InterestViewModel> _logger;

    private ObservableCollection<BorrowerSummary> _borrowers = new();
    private ObservableCollection<BorrowerSummary> _searchResults = new();
    private BorrowerSummary? _selectedBorrower;
    private string _searchQueryText = string.Empty;
    private DateTimeOffset? _calculationDate = DateTimeOffset.Now.Date;
    private string _accountStatus = string.Empty;
    private DateTime? _closedDate;
    private decimal _openingPrincipal;
    private decimal _currentPrincipal;
    private decimal _totalDeposits;
    private decimal _totalWithdrawals;
    private decimal _totalInterest;
    private decimal _finalOutstanding;
    private ObservableCollection<InterestCalculationSegment> _calculationSegments = new();
    private bool _isCalculating;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _calculationStatus = string.Empty;
    private InterestCalculationResult? _calculationResult;

    public InterestViewModel(
        IInterestCalculationService interestService,
        IBorrowerService borrowerService,
        DhirDhar.Application.Localization.ILocalizationService localizationService,
        ITranslationService translationService,
        ILogger<InterestViewModel> logger)
    {
        _interestService = interestService;
        _borrowerService = borrowerService;
        _localizationService = localizationService;
        _translationService = translationService;
        _logger = logger;

        AttachLocalization(localizationService);

        _localizationService.LanguageChanged += (s, e) =>
        {
            OnPropertyChanged(string.Empty);
            _ = LoadBorrowersAsync();
        };

        CalculateCommand = new RelayCommand(async () => await CalculateAsync(), () => CanCalculate);
        RefreshCommand = new RelayCommand(async () => await LoadBorrowersAsync());
        RetryCommand = new RelayCommand(async () => await CalculateAsync(), () => CanCalculate);
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
            if (SetProperty(ref _selectedBorrower, value))
            {
                OnPropertyChanged(nameof(CanCalculate));
                OnPropertyChanged(nameof(HasSelectedBorrower));
                OnPropertyChanged(nameof(EmptyStateMessage));
                CalculateCommand?.RaiseCanExecuteChanged();
                RetryCommand?.RaiseCanExecuteChanged();
                ResetCalculation();
                if (value is not null)
                {
                    _ = LoadBorrowerDetailsAsync(value.Id);
                }
            }
        }
    }

    public bool HasSelectedBorrower => SelectedBorrower is not null;

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
        var englishQ = ScriptTranslator.ToEnglish(cleanQ).Trim();
        var gujaratiQ = ScriptTranslator.ToGujarati(cleanQ).Trim();
        var hindiQ = ScriptTranslator.ToHindi(cleanQ).Trim();
        var asciiDigits = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(cleanQ);

        bool Matches(BorrowerSummary b)
        {
            return (!string.IsNullOrEmpty(b.FullName) && (b.FullName.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.FullName.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(gujaratiQ) && b.FullName.Contains(gujaratiQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(hindiQ) && b.FullName.Contains(hindiQ, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.Name) && (b.Name.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.Name.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(gujaratiQ) && b.Name.Contains(gujaratiQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(hindiQ) && b.Name.Contains(hindiQ, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.BorrowerNumber) && (b.BorrowerNumber.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.BorrowerNumber.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(asciiDigits) && b.BorrowerNumber.Contains(asciiDigits, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.Contact) && (b.Contact.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(asciiDigits) && b.Contact.Contains(asciiDigits, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.FatherName) && (b.FatherName.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.FatherName.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(gujaratiQ) && b.FatherName.Contains(gujaratiQ, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.Surname) && (b.Surname.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.Surname.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(gujaratiQ) && b.Surname.Contains(gujaratiQ, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.Village) && (b.Village.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.Village.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(gujaratiQ) && b.Village.Contains(gujaratiQ, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.AadharNumber) && (b.AadharNumber.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(asciiDigits) && b.AadharNumber.Contains(asciiDigits, StringComparison.OrdinalIgnoreCase))));
        }

        var matches = System.Linq.Enumerable.Where(_borrowers, Matches).ToList();
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
        CalculationDate = DateTimeOffset.Now.Date;
    }

    public DateTimeOffset? CalculationDate
    {
        get => _calculationDate;
        set
        {
            if (SetProperty(ref _calculationDate, value))
            {
                OnPropertyChanged(nameof(CanCalculate));
                CalculateCommand?.RaiseCanExecuteChanged();
                RetryCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string AccountStatus
    {
        get => _accountStatus;
        private set => SetProperty(ref _accountStatus, value);
    }

    public DateTime? ClosedDate
    {
        get => _closedDate;
        private set => SetProperty(ref _closedDate, value);
    }

    public decimal OpeningPrincipal
    {
        get => _openingPrincipal;
        private set => SetProperty(ref _openingPrincipal, value);
    }

    public decimal CurrentPrincipal
    {
        get => _currentPrincipal;
        private set => SetProperty(ref _currentPrincipal, value);
    }

    public decimal TotalDeposits
    {
        get => _totalDeposits;
        private set => SetProperty(ref _totalDeposits, value);
    }

    public decimal TotalWithdrawals
    {
        get => _totalWithdrawals;
        private set => SetProperty(ref _totalWithdrawals, value);
    }

    public decimal TotalInterest
    {
        get => _totalInterest;
        private set => SetProperty(ref _totalInterest, value);
    }

    public decimal FinalOutstanding
    {
        get => _finalOutstanding;
        private set => SetProperty(ref _finalOutstanding, value);
    }

    public ObservableCollection<InterestCalculationSegment> CalculationSegments
    {
        get => _calculationSegments;
        private set => SetProperty(ref _calculationSegments, value);
    }

    public bool IsCalculating
    {
        get => _isCalculating;
        private set
        {
            if (SetProperty(ref _isCalculating, value))
            {
                OnPropertyChanged(nameof(CanCalculate));
                CalculateCommand?.RaiseCanExecuteChanged();
                RetryCommand?.RaiseCanExecuteChanged();
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

    public string CalculationStatus
    {
        get => _calculationStatus;
        private set => SetProperty(ref _calculationStatus, value);
    }

    public bool CanCalculate => SelectedBorrower is not null && !IsCalculating;

    public bool HasSegments => CalculationSegments.Count > 0;

    public bool HasCalculationResult => _calculationResult is not null;
    public bool HasResult => HasCalculationResult;

    public bool HasExecutedCalculation => _calculationResult is not null;

    public bool ShowEmptyState => HasExecutedCalculation && CalculationSegments.Count == 0;

    public string PageTitle => _localizationService.GetString("Interest");
    public string PageSubtitle => _localizationService.GetString("InterestPageSubtitle");
    public string RefreshText => _localizationService.GetString("Refresh");
    public string TotalDepositsLabel => _localizationService.GetString("TotalDeposits");
    public string TotalWithdrawalsLabel => _localizationService.GetString("TotalWithdrawals");
    public string OpeningPrincipalLabel => _localizationService.GetString("CurrentBalance");
    public string TotalInterestLabel => _localizationService.GetString("TotalInterest");
    public string FinalOutstandingLabel => _localizationService.GetString("TotalOutstanding");
    public string RetryText => _localizationService.GetString("Retry");
    public string SearchBorrowerLabel => _localizationService.GetString("SearchBorrower");
    public string SearchBorrowerPlaceholder => _localizationService.GetString("SearchBorrowerPlaceholder");
    public string SelectBorrowerLabel => _localizationService.GetString("SelectBorrower");
    public string CalculationDateLabel => _localizationService.GetString("CalculationDate");
    public string CalculateLabel => _localizationService.GetString("Calculate");
    public string CalculationSegmentsLabel => _localizationService.GetString("CalculationSegments");
    public string StartLabel => _localizationService.GetString("Start");
    public string EndLabel => _localizationService.GetString("End");
    public string PrincipalLabel => _localizationService.GetString("Principal");
    public string RatePercentLabel => _localizationService.GetString("RatePercent");
    public string DaysLabel => _localizationService.GetString("Days");
    public string DaysPerMonthLabel => _localizationService.GetString("DaysPerMonth");
    public string InterestColumnLabel => _localizationService.GetString("Interest");
    public string TransactionColumnLabel => _localizationService.GetString("Transaction");
    public string AmountColumnLabel => _localizationService.GetString("Amount");
    public string ClosingColumnLabel => _localizationService.GetString("Closing");
    public string AccountClosedLabel => _localizationService.GetString("AccountClosed");
    public string ClosedDateLabel => _localizationService.GetString("ClosedDate");
    public string InterestCalculatedUntilLabel => _localizationService.GetString("InterestCalculatedUntil");
    public string NoInterestAfterClosedLabel => _localizationService.GetString("NoInterestAfterClosed");
    public string SelectBorrowerPromptLabel => _localizationService.GetString("SelectBorrowerPrompt");
    public string NoCalculationDataLabel => _localizationService.GetString("NoCalculationData");

    public string EmptyStateMessage => SelectedBorrower is null
        ? SelectBorrowerPromptLabel
        : NoCalculationDataLabel;

    public string FormattedOpeningPrincipal => LCur(OpeningPrincipal);
    public string FormattedCurrentPrincipal => LCur(CurrentPrincipal);
    public string FormattedTotalDeposits => LCur(TotalDeposits);
    public string FormattedTotalWithdrawals => LCur(TotalWithdrawals);
    public string FormattedTotalInterest => LCur(TotalInterest);
    public string FormattedCalculatedInterest => FormattedTotalInterest;
    public string FormattedFinalOutstanding => LCur(FinalOutstanding);
    public string FormattedTotalPayable => FormattedFinalOutstanding;
    public string FormattedClosedDate => LDate(ClosedDate, "dd-MMM-yyyy");
    public bool IsClosed => _calculationResult?.IsClosed ?? false;

    public string RetryLabel => RetryText;
    public string BreakdownLabel => CalculationSegmentsLabel;
    public string CalculatedInterestLabel => TotalInterestLabel;
    public string CurrentPrincipalLabel => OpeningPrincipalLabel;
    public string TotalPayableLabel => FinalOutstandingLabel;

    public RelayCommand CalculateCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand RetryCommand { get; }

    public async Task LoadBorrowersAsync()
    {
        try
        {
            var result = await _borrowerService.GetListAsync(BorrowerFilter.All, null, 1, 1000).ConfigureAwait(false);
            var currentLang = _localizationService.CurrentLanguage;
            var items = result.Items.Localize(_translationService, currentLang).ToList();

            Borrowers = new ObservableCollection<BorrowerSummary>(items);
            SearchResults = new ObservableCollection<BorrowerSummary>(items);

            if (_selectedBorrower is not null)
            {
                var existingId = _selectedBorrower.Id;
                var updated = items.FirstOrDefault(b => b.Id == existingId);
                if (updated is not null)
                {
                    _selectedBorrower = updated;
                    OnPropertyChanged(nameof(SelectedBorrower));
                }
            }
            else if (!string.IsNullOrWhiteSpace(SearchQueryText))
            {
                SearchBorrowers(SearchQueryText);
            }

            OnPropertyChanged(nameof(CanCalculate));
            CalculateCommand?.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load borrowers for interest calculation.");
        }
    }

    private async Task LoadBorrowerDetailsAsync(Guid borrowerId)
    {
        try
        {
            var borrower = await _borrowerService.GetByIdAsync(borrowerId).ConfigureAwait(false);
            if (borrower is not null)
            {
                AccountStatus = borrower.Status;
                OpeningPrincipal = borrower.OutstandingAmount;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load borrower details for interest.");
        }
    }

    private void ResetCalculation()
    {
        _calculationResult = null;
        CurrentPrincipal = 0;
        TotalInterest = 0;
        FinalOutstanding = 0;
        CalculationSegments = new ObservableCollection<InterestCalculationSegment>();
        CalculationStatus = string.Empty;
        ClosedDate = null;
        OnPropertyChanged(nameof(HasSegments));
        OnPropertyChanged(nameof(HasCalculationResult));
        OnPropertyChanged(nameof(HasExecutedCalculation));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(FormattedCurrentPrincipal));
        OnPropertyChanged(nameof(FormattedTotalInterest));
        OnPropertyChanged(nameof(FormattedFinalOutstanding));
        OnPropertyChanged(nameof(FormattedClosedDate));
        OnPropertyChanged(nameof(IsClosed));
    }

    public async Task CalculateAsync()
    {
        if (SelectedBorrower is null || IsCalculating)
        {
            return;
        }

        IsCalculating = true;
        HasError = false;
        ErrorMessage = string.Empty;
        CalculationStatus = _localizationService.GetString("CalculatingInterest");

        try
        {
            var effectiveDate = CalculationDate?.DateTime ?? DateTime.Today;

            var result = await _interestService.CalculateAsync(SelectedBorrower.Id, effectiveDate).ConfigureAwait(false);

            _calculationResult = result;
            CurrentPrincipal = result.ClosingPrincipal;
            TotalInterest = result.TotalInterest;
            FinalOutstanding = result.TotalOutstanding;
            AccountStatus = result.Status;
            ClosedDate = result.ClosedDate;
            IsCalculating = false;

            CalculationSegments = new ObservableCollection<InterestCalculationSegment>(result.Segments);

            CalculationStatus = result.IsClosed
                ? $"{string.Format(_localizationService.GetString("InterestCalculatedUntilStatus"), LDate(result.CalculationEndDate, "dd-MMM-yyyy"))} ({_localizationService.GetString("AccountClosed")})"
                : string.Format(_localizationService.GetString("InterestCalculatedUntilStatus"), LDate(result.CalculationEndDate, "dd-MMM-yyyy"));

            OnPropertyChanged(nameof(FormattedCurrentPrincipal));
            OnPropertyChanged(nameof(FormattedTotalInterest));
            OnPropertyChanged(nameof(FormattedFinalOutstanding));
            OnPropertyChanged(nameof(FormattedClosedDate));
            OnPropertyChanged(nameof(IsClosed));
            OnPropertyChanged(nameof(HasSegments));
            OnPropertyChanged(nameof(HasCalculationResult));
            OnPropertyChanged(nameof(HasExecutedCalculation));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(EmptyStateMessage));

            _logger.LogInformation("Interest calculated for borrower '{BorrowerId}'. TotalInterest={TotalInterest}.", SelectedBorrower.Id, TotalInterest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate interest for borrower '{BorrowerId}'.", SelectedBorrower?.Id);
            HasError = true;
            ErrorMessage = _localizationService.GetString("InterestCalculationFailed");
            CalculationStatus = _localizationService.GetString("CalculationFailed");
        }
        finally
        {
            IsCalculating = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Localization;

using BorrowerSummaryDto = DhirDhar.Application.Borrowers.Models.BorrowerSummary;
using DhirDhar.Application.Transactions;
using DhirDhar.Application.Transactions.Models;
using DhirDhar.Desktop.Navigation;
using DhirDhar.Desktop.ViewModels;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.ViewModels.Transactions;

public sealed class TransactionRowItem
{
    private readonly DhirDhar.Application.Localization.ILocalizationService? _localizationService;

    public Guid Id { get; }
    public Guid? BorrowerId { get; }
    public string BorrowerName { get; }
    public string BorrowerNumber { get; }
    public string FormattedBorrowerInfo
    {
        get
        {
            return !string.IsNullOrWhiteSpace(BorrowerNumber) ? $"{BorrowerName} (#{BorrowerNumber})" : BorrowerName;
        }
    }
    public string RawType { get; }
    public string DisplayType { get; }
    public string LocalizedDisplayType => _localizationService != null ? _localizationService.GetString(DisplayType) : DisplayType;
    public decimal Amount { get; }
    public string FormattedAmount => _localizationService is null ? $"₹ {Amount:N2}" : _localizationService.ToLocalizedCurrency(Amount);
    public DateTime Date { get; }
    public string FormattedDate => _localizationService is null ? Date.ToString("dd/MM/yyyy") : _localizationService.ToLocalizedDate(Date, "dd/MM/yyyy");
    public string Description { get; }
    public decimal RunningBalance { get; }
    public string FormattedRunningBalance => _localizationService is null ? $"₹ {RunningBalance:N2}" : (_localizationService.ToLocalizedCurrency(RunningBalance));
    public bool IsDeposit => string.Equals(RawType, "Deposit", StringComparison.OrdinalIgnoreCase);
    public bool IsWithdrawal => string.Equals(RawType, "Withdrawal", StringComparison.OrdinalIgnoreCase);
    public bool IsInterest => string.Equals(RawType, "Interest", StringComparison.OrdinalIgnoreCase);
    public bool IsInitialLoan { get; }

    public string SemanticBrushKey => IsInitialLoan || IsDeposit ? "SuccessBrush" : (IsInterest ? "PrimaryBrush" : "ErrorBrush");

    public TransactionRowItem(
        Guid id,
        Guid? borrowerId,
        string borrowerName,
        string? borrowerNumber,
        string rawType,
        string displayType,
        decimal amount,
        DateTime date,
        string? description,
        decimal runningBalance,
        bool isInitialLoan = false,
        DhirDhar.Application.Localization.ILocalizationService? localizationService = null)
    {
        Id = id;
        BorrowerId = borrowerId;
        BorrowerName = borrowerName;
        BorrowerNumber = borrowerNumber ?? string.Empty;
        RawType = rawType;
        DisplayType = displayType;
        Amount = amount;
        Date = date;
        Description = description ?? string.Empty;
        RunningBalance = runningBalance;
        IsInitialLoan = isInitialLoan;
        _localizationService = localizationService;
    }
}

public sealed record TransactionTypeOption(string Value, string Label);

public sealed class TransactionsViewModel : ViewModelBase
{
    private readonly ITransactionService _transactionService;
    private readonly IBorrowerService _borrowerService;
    private readonly INavigationService _navigationService;
    private readonly DhirDhar.Application.Localization.ILocalizationService _localizationService;
    private readonly ITranslationService _translationService;
    private readonly ILogger<TransactionsViewModel> _logger;

    private ObservableCollection<TransactionSummary> _transactions = new();
    private ObservableCollection<TransactionRowItem> _displayTransactions = new();
    private ObservableCollection<BorrowerSummaryDto> _borrowers = new();
    private TransactionFinancials? _financials;
    private Guid? _selectedBorrowerId;
    private string _selectedTransactionType = "All";
    private string _searchTerm = string.Empty;
    private DateTime? _startDate;
    private DateTime? _endDate;
    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage = string.Empty;

    // New transaction fields
    private Guid? _newBorrowerId;
    private BorrowerSummaryDto? _selectedNewBorrower;
    private string _newBorrowerSearchQuery = string.Empty;
    private ObservableCollection<BorrowerSummaryDto> _newBorrowerSearchResults = new();
    private string _newType = "Deposit";
    private decimal _newAmount;
    private string _newAmountText = string.Empty;
    private DateTimeOffset? _newOccurredOn = DateTimeOffset.Now.Date;
    private string? _newNotes;
    private string? _newReference;
    private bool _isAddingTransaction;

    public ObservableCollection<TransactionTypeOption> TransactionTypeOptions => new()
    {
        new("Withdrawal", _localizationService.GetString("Withdrawal")),
        new("Deposit", _localizationService.GetString("Deposit"))
    };

    public TransactionsViewModel(
        ITransactionService transactionService,
        IBorrowerService borrowerService,
        INavigationService navigationService,
        DhirDhar.Application.Localization.ILocalizationService localizationService,
        ITranslationService translationService,
        ILogger<TransactionsViewModel> logger)
    {
        _transactionService = transactionService;
        _borrowerService = borrowerService;
        _navigationService = navigationService;
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
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        CreateTransactionCommand = new RelayCommand(async () => await CreateTransactionAsync());
        ToggleAddTransactionCommand = new RelayCommand(() =>
        {
            IsAddingTransaction = !IsAddingTransaction;
            if (IsAddingTransaction && (NewBorrowerSearchResults.Count == 0 || string.IsNullOrWhiteSpace(NewBorrowerSearchQuery)))
            {
                NewBorrowerSearchResults = new ObservableCollection<BorrowerSummaryDto>(Borrowers);
            }
        });

        SelectAllFilterCommand = new RelayCommand(() => SelectedTransactionType = "All");
        SelectDepositsFilterCommand = new RelayCommand(() => SelectedTransactionType = "Deposit");
        SelectWithdrawalsFilterCommand = new RelayCommand(() => SelectedTransactionType = "Withdrawal");
        SelectInterestFilterCommand = new RelayCommand(() => SelectedTransactionType = "Interest");

        OpenTransactionDetailsCommand = new RelayCommand<TransactionRowItem>(OpenTransactionDetails);
    }

    public ObservableCollection<TransactionSummary> Transactions
    {
        get => _transactions;
        private set => SetProperty(ref _transactions, value);
    }

    public ObservableCollection<TransactionRowItem> DisplayTransactions
    {
        get => _displayTransactions;
        private set
        {
            if (SetProperty(ref _displayTransactions, value))
            {
                OnPropertyChanged(nameof(HasTransactions));
                OnPropertyChanged(nameof(HasNoTransactions));
            }
        }
    }

    public bool HasTransactions => DisplayTransactions.Count > 0;
    public bool HasNoTransactions => DisplayTransactions.Count == 0;

    public ObservableCollection<BorrowerSummaryDto> Borrowers
    {
        get => _borrowers;
        private set => SetProperty(ref _borrowers, value);
    }

    public TransactionFinancials? Financials
    {
        get => _financials;
        private set => SetProperty(ref _financials, value);
    }

    public Guid? SelectedBorrowerId
    {
        get => _selectedBorrowerId;
        set
        {
            if (SetProperty(ref _selectedBorrowerId, value))
            {
                _ = LoadAsync();
            }
        }
    }

    public string SelectedTransactionType
    {
        get => _selectedTransactionType;
        set
        {
            if (SetProperty(ref _selectedTransactionType, value))
            {
                OnPropertyChanged(nameof(IsAllFilterSelected));
                OnPropertyChanged(nameof(IsDepositFilterSelected));
                OnPropertyChanged(nameof(IsWithdrawalFilterSelected));
                OnPropertyChanged(nameof(IsInterestFilterSelected));
                _ = LoadAsync();
            }
        }
    }

    public bool IsAllFilterSelected => string.Equals(SelectedTransactionType, "All", StringComparison.OrdinalIgnoreCase);
    public bool IsDepositFilterSelected => string.Equals(SelectedTransactionType, "Deposit", StringComparison.OrdinalIgnoreCase);
    public bool IsWithdrawalFilterSelected => string.Equals(SelectedTransactionType, "Withdrawal", StringComparison.OrdinalIgnoreCase);
    public bool IsInterestFilterSelected => string.Equals(SelectedTransactionType, "Interest", StringComparison.OrdinalIgnoreCase);

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchDebounceCts;

    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (SetProperty(ref _searchTerm, value))
            {
                TriggerDebouncedLoad();
            }
        }
    }

    private void TriggerDebouncedLoad()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var ct = _searchDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(SearchTerm))
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

    public DateTime? StartDate
    {
        get => _startDate;
        set
        {
            if (SetProperty(ref _startDate, value))
            {
                _ = LoadAsync();
            }
        }
    }

    public DateTime? EndDate
    {
        get => _endDate;
        set
        {
            if (SetProperty(ref _endDate, value))
            {
                _ = LoadAsync();
            }
        }
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

    public string PageTitle => _localizationService.GetString("Transactions");
    public string PageSubtitle => _localizationService.GetString("TransactionsSubtitle");
    public string NewTransactionLabel => _localizationService.GetString("NewTransaction");
    public string RefreshLabel => _localizationService.GetString("Refresh");
    public string TotalDepositsLabel => _localizationService.GetString("TotalDeposits");
    public string ClickToFilterDepositsLabel => _localizationService.GetString("ClickToFilterDeposits");
    public string TotalWithdrawalsLabel => _localizationService.GetString("TotalWithdrawals");
    public string ClickToFilterWithdrawalsLabel => _localizationService.GetString("ClickToFilterWithdrawals");
    public string NetPositionLabel => _localizationService.GetString("NetPosition");
    public string ClickToViewAllLabel => _localizationService.GetString("ClickToViewAll");
    public string SearchPlaceholder => _localizationService.GetString("SearchBorrowersPlaceholder");
    public string AllLabel => _localizationService.GetString("All");
    public string GivenLoanLabel => _localizationService.GetString("GivenLoan");
    public string ReceivedDepositLabel => _localizationService.GetString("ReceivedDeposit");
    public string ClearLabel => _localizationService.GetString("Clear");
    public string RecentTransactionsLabel => _localizationService.GetString("RecentTransactions");
    public string NewTransactionEntryLabel => _localizationService.GetString("NewTransactionEntry");
    public string BorrowerSearchLabel => _localizationService.GetString("BorrowerSearch");
    public string BorrowerSearchPlaceholder => _localizationService.GetString("BorrowerSearchPlaceholder");
    public string BorrowerLabel => _localizationService.GetString("Borrower");
    public string SelectBorrowerLabel => _localizationService.GetString("SelectBorrower");
    public string TypeLabel => _localizationService.GetString("Type");
    public string TransactionDateLabel => _localizationService.GetString("TransactionDate");
    public string AmountLabel => _localizationService.GetString("Amount");
    public string NotesDescriptionLabel => _localizationService.GetString("NotesDescription");
    public string EnterReferenceLabel => _localizationService.GetString("EnterReference");
    public string CancelLabel => _localizationService.GetString("Cancel");
    public string SaveTransactionLabel => _localizationService.GetString("SaveTransaction");
    public string NoTransactionsTitleLabel => _localizationService.GetString("NoTransactions");
    public string NoTransactionsDescLabel => _localizationService.GetString("NoTransactionsDesc");

    public Guid? NewBorrowerId
    {
        get => _newBorrowerId;
        set => SetProperty(ref _newBorrowerId, value);
    }

    public BorrowerSummaryDto? SelectedNewBorrower
    {
        get => _selectedNewBorrower;
        set
        {
            if (SetProperty(ref _selectedNewBorrower, value))
            {
                NewBorrowerId = value?.Id;
            }
        }
    }

    public string NewBorrowerSearchQuery
    {
        get => _newBorrowerSearchQuery;
        set
        {
            if (SetProperty(ref _newBorrowerSearchQuery, value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _selectedNewBorrower = null;
                    _newBorrowerId = null;
                    OnPropertyChanged(nameof(SelectedNewBorrower));
                    OnPropertyChanged(nameof(NewBorrowerId));
                }
            }
        }
    }

    public ObservableCollection<BorrowerSummaryDto> NewBorrowerSearchResults
    {
        get => _newBorrowerSearchResults;
        private set => SetProperty(ref _newBorrowerSearchResults, value);
    }

    public string NewType
    {
        get => _newType;
        set => SetProperty(ref _newType, value);
    }

    public decimal NewAmount
    {
        get => _newAmount;
        set
        {
            if (SetProperty(ref _newAmount, value))
            {
                var text = value > 0 ? value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
                if (_newAmountText != text)
                {
                    _newAmountText = text;
                    OnPropertyChanged(nameof(NewAmountText));
                }
            }
        }
    }

    public string NewAmountText
    {
        get => _newAmountText;
        set
        {
            if (SetProperty(ref _newAmountText, value ?? string.Empty))
            {
                if (MonetaryAmountParser.TryParse(value, out var parsed))
                {
                    if (_newAmount != parsed)
                    {
                        _newAmount = parsed;
                        OnPropertyChanged(nameof(NewAmount));
                    }
                }
                else
                {
                    if (_newAmount != 0m)
                    {
                        _newAmount = 0m;
                        OnPropertyChanged(nameof(NewAmount));
                    }
                }
            }
        }
    }

    public DateTimeOffset? NewOccurredOn
    {
        get => _newOccurredOn;
        set => SetProperty(ref _newOccurredOn, value);
    }

    public string? NewNotes
    {
        get => _newNotes;
        set => SetProperty(ref _newNotes, value);
    }

    public string? NewReference
    {
        get => _newReference;
        set => SetProperty(ref _newReference, value);
    }

    public bool IsAddingTransaction
    {
        get => _isAddingTransaction;
        set => SetProperty(ref _isAddingTransaction, value);
    }

    public string FormattedTotalDeposits => Financials is not null ? LCur(Financials.TotalDeposits) : LCur(0m);
    public string FormattedTotalWithdrawals => Financials is not null ? LCur(Financials.TotalWithdrawals) : LCur(0m);
    public string FormattedNetFinancialPosition
    {
        get
        {
            if (Financials is null) return LCur(0m);
            var net = Financials.TotalDeposits - Financials.TotalWithdrawals;
            return net < 0m ? LCurNegative(net) : LCur(net);
        }
    }

    public RelayCommand LoadCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }
    public RelayCommand CreateTransactionCommand { get; }
    public RelayCommand ToggleAddTransactionCommand { get; }
    public RelayCommand SelectAllFilterCommand { get; }
    public RelayCommand SelectDepositsFilterCommand { get; }
    public RelayCommand SelectWithdrawalsFilterCommand { get; }
    public RelayCommand SelectInterestFilterCommand { get; }
    public RelayCommand<TransactionRowItem> OpenTransactionDetailsCommand { get; }

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

        try
        {
            var currentLang = _localizationService.CurrentLanguage;
            var borrowersResult = await _borrowerService.GetListAsync(BorrowerFilter.All, null, 1, 1000, cancellationToken);
            var localizedBorrowers = borrowersResult.Items.Localize(_translationService, currentLang);
            Borrowers = new ObservableCollection<BorrowerSummaryDto>(localizedBorrowers);
            if (NewBorrowerSearchResults.Count == 0 || string.IsNullOrWhiteSpace(NewBorrowerSearchQuery))
            {
                NewBorrowerSearchResults = new ObservableCollection<BorrowerSummaryDto>(localizedBorrowers);
            }

            var typeFilter = SelectedTransactionType switch
            {
                "Deposit" => TransactionTypeFilter.Deposit,
                "Withdrawal" => TransactionTypeFilter.Withdrawal,
                _ => TransactionTypeFilter.All
            };

            var filter = new TransactionFilterRequest(
                SelectedBorrowerId == Guid.Empty ? null : SelectedBorrowerId,
                typeFilter,
                StartDate,
                EndDate,
                SearchTerm,
                1,
                1000);

            var listResult = await _transactionService.GetListAsync(filter, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            Transactions = new ObservableCollection<TransactionSummary>(listResult.Items);
            Financials = await _transactionService.GetFinancialsAsync(SelectedBorrowerId, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            var chronologicalList = listResult.Items.OrderBy(t => t.TransactionDate).ThenBy(t => t.CreatedAt).ToList();
            var totalDeposits = 0m;
            var totalWithdrawals = 0m;
            var runningBalance = 0m;

            var rowItems = new List<TransactionRowItem>();

            foreach (var txn in chronologicalList)
            {
                var isDeposit = string.Equals(txn.TransactionType, "Deposit", StringComparison.OrdinalIgnoreCase);
                var isWithdrawal = string.Equals(txn.TransactionType, "Withdrawal", StringComparison.OrdinalIgnoreCase);
                var isInterest = string.Equals(txn.TransactionType, "Interest", StringComparison.OrdinalIgnoreCase);
                var isInitialLoan = txn.Description?.Contains("Initial Loan Amount", StringComparison.OrdinalIgnoreCase) == true
                                    || txn.Description?.Contains("Initial Principal", StringComparison.OrdinalIgnoreCase) == true
                                    || txn.Description?.Contains("INIT-", StringComparison.OrdinalIgnoreCase) == true;

                if (isDeposit)
                {
                    totalDeposits += txn.Amount;
                    runningBalance -= txn.Amount;
                }
                else
                {
                    totalWithdrawals += txn.Amount;
                    runningBalance += txn.Amount;
                }

                string displayType;
                if (isInitialLoan)
                {
                    displayType = "Given (Loan)";
                }
                else if (isDeposit)
                {
                    displayType = "Received (Deposit)";
                }
                else if (isWithdrawal)
                {
                    displayType = "Given (Withdrawal)";
                }
                else if (isInterest)
                {
                    displayType = "Interest Accrued";
                }
                else
                {
                    displayType = txn.TransactionType;
                }

                var borrowerName = _translationService.Translate(txn.BorrowerName, currentLang);
                var localizedDescription = _localizationService.LocalizeText(txn.Description, currentLang);

                rowItems.Add(new TransactionRowItem(
                    txn.Id,
                    txn.BorrowerId,
                    borrowerName,
                    txn.BorrowerNumber,
                    txn.TransactionType,
                    displayType,
                    txn.Amount,
                    txn.TransactionDate,
                    localizedDescription,
                    runningBalance,
                    isInitialLoan,
                    _localizationService));
            }

            // Display newest transactions first
            var newestFirstList = rowItems.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList();
            DisplayTransactions = new ObservableCollection<TransactionRowItem>(newestFirstList);

            OnPropertyChanged(nameof(FormattedTotalDeposits));
            OnPropertyChanged(nameof(FormattedTotalWithdrawals));
            OnPropertyChanged(nameof(FormattedNetFinancialPosition));

            _logger.LogInformation("Loaded TransactionsPage successfully. Count={Count}.", DisplayTransactions.Count);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load transactions.");
            HasError = true;
            ErrorMessage = _localizationService.GetString("TransactionsLoadFailed");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    public async Task CreateTransactionAsync()
    {
        if (!NewBorrowerId.HasValue || NewBorrowerId.Value == Guid.Empty)
        {
            HasError = true;
            ErrorMessage = _localizationService.GetString("InvalidBorrowerSelected");
            return;
        }

        decimal effectiveAmount = _newAmount;
        if (effectiveAmount <= 0m && !string.IsNullOrWhiteSpace(_newAmountText))
        {
            if (MonetaryAmountParser.TryParse(_newAmountText, out var fallbackParsed) && fallbackParsed > 0m)
            {
                effectiveAmount = fallbackParsed;
            }
        }

        if (effectiveAmount <= 0m)
        {
            HasError = true;
            ErrorMessage = _localizationService.GetString("InvalidAmount");
            return;
        }

        var rawType = NewType ?? string.Empty;
        var transactionType = rawType.Contains("Withdrawal", StringComparison.OrdinalIgnoreCase) || rawType.Contains("Given", StringComparison.OrdinalIgnoreCase)
            ? DhirDhar.Domain.Enums.TransactionType.Withdrawal
            : DhirDhar.Domain.Enums.TransactionType.Deposit;

        var occurredOn = NewOccurredOn?.DateTime ?? DateTime.Now;

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            string? description = !string.IsNullOrWhiteSpace(NewReference)
                ? (!string.IsNullOrWhiteSpace(NewNotes) ? $"{NewReference} - {NewNotes}" : NewReference)
                : NewNotes;

            var request = new CreateTransactionRequest(
                NewBorrowerId.Value,
                transactionType,
                effectiveAmount,
                occurredOn,
                description);

            await _transactionService.CreateAsync(request);

            IsAddingTransaction = false;
            NewAmount = 0m;
            NewAmountText = string.Empty;
            NewOccurredOn = DateTimeOffset.Now.Date;
            NewNotes = null;
            NewReference = null;
            SelectedNewBorrower = null;
            NewBorrowerSearchQuery = string.Empty;

            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create transaction.");
            HasError = true;
            ErrorMessage = _localizationService.GetString("TransactionSaveFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public static bool TryParseMonetaryAmount(string? input, out decimal amount) => MonetaryAmountParser.TryParse(input, out amount);

    public void SearchNewBorrowers(string query)
    {
        _newBorrowerSearchQuery = query ?? string.Empty;
        OnPropertyChanged(nameof(NewBorrowerSearchQuery));

        if (string.IsNullOrWhiteSpace(query))
        {
            _selectedNewBorrower = null;
            _newBorrowerId = null;
            OnPropertyChanged(nameof(SelectedNewBorrower));
            OnPropertyChanged(nameof(NewBorrowerId));
            NewBorrowerSearchResults = new ObservableCollection<BorrowerSummaryDto>(_borrowers);
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

        bool Matches(BorrowerSummaryDto b)
        {
            return (!string.IsNullOrEmpty(b.FullName) && (b.FullName.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.FullName.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(gujaratiQ) && b.FullName.Contains(gujaratiQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(hindiQ) && b.FullName.Contains(hindiQ, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.Name) && (b.Name.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.Name.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(gujaratiQ) && b.Name.Contains(gujaratiQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(hindiQ) && b.Name.Contains(hindiQ, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.BorrowerNumber) && (b.BorrowerNumber.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.BorrowerNumber.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(asciiDigits) && b.BorrowerNumber.Contains(asciiDigits, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.FormattedBorrowerNumber) && (b.FormattedBorrowerNumber.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.FormattedBorrowerNumber.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(asciiDigits) && b.FormattedBorrowerNumber.Contains(asciiDigits, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.Contact) && (b.Contact.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(asciiDigits) && b.Contact.Contains(asciiDigits, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.FatherName) && (b.FatherName.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.FatherName.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(gujaratiQ) && b.FatherName.Contains(gujaratiQ, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.Surname) && (b.Surname.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.Surname.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(gujaratiQ) && b.Surname.Contains(gujaratiQ, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.Village) && (b.Village.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(englishQ) && b.Village.Contains(englishQ, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(gujaratiQ) && b.Village.Contains(gujaratiQ, StringComparison.OrdinalIgnoreCase)))) ||
                   (!string.IsNullOrEmpty(b.AadharNumber) && (b.AadharNumber.Contains(cleanQ, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(asciiDigits) && b.AadharNumber.Contains(asciiDigits, StringComparison.OrdinalIgnoreCase))));
        }

        var matches = _borrowers.Where(Matches).ToList();
        NewBorrowerSearchResults = new ObservableCollection<BorrowerSummaryDto>(matches);

        var exactMatch = matches.FirstOrDefault(b =>
            string.Equals(b.BorrowerNumber, cleanQ, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b.FormattedBorrowerNumber, cleanQ, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b.FullName, cleanQ, StringComparison.OrdinalIgnoreCase) ||
            string.Equals($"{b.FullName} ({b.FormattedBorrowerNumber})", cleanQ, StringComparison.OrdinalIgnoreCase) ||
            string.Equals($"{b.FullName} ({b.BorrowerNumber})", cleanQ, StringComparison.OrdinalIgnoreCase));

        if (exactMatch is not null && _selectedNewBorrower?.Id != exactMatch.Id)
        {
            SelectNewBorrower(exactMatch);
        }
        else if (exactMatch is null && _selectedNewBorrower is not null)
        {
            var currentFormatted = $"{_selectedNewBorrower.FullName} ({_selectedNewBorrower.FormattedBorrowerNumber})";
            if (!string.Equals(cleanQ, currentFormatted, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(cleanQ, _selectedNewBorrower.FullName, StringComparison.OrdinalIgnoreCase))
            {
                _selectedNewBorrower = null;
                _newBorrowerId = null;
                OnPropertyChanged(nameof(SelectedNewBorrower));
                OnPropertyChanged(nameof(NewBorrowerId));
            }
        }
    }

    public void SelectNewBorrower(BorrowerSummaryDto? borrower)
    {
        _selectedNewBorrower = borrower;
        _newBorrowerId = borrower?.Id;
        OnPropertyChanged(nameof(SelectedNewBorrower));
        OnPropertyChanged(nameof(NewBorrowerId));

        if (borrower is not null)
        {
            _newBorrowerSearchQuery = $"{borrower.FullName} ({borrower.FormattedBorrowerNumber})";
            OnPropertyChanged(nameof(NewBorrowerSearchQuery));
        }
    }

    private void OpenTransactionDetails(TransactionRowItem? item)
    {
        if (item?.BorrowerId.HasValue == true && item.BorrowerId.Value != Guid.Empty)
        {
            _navigationService.Navigate(NavigationDestination.BorrowerDetails, item.BorrowerId.Value);
        }
    }

    private void ClearFilters()
    {
        _loadCts?.Cancel();
        _searchDebounceCts?.Cancel();
        _selectedBorrowerId = null;
        _selectedTransactionType = "All";
        _searchTerm = string.Empty;
        _startDate = null;
        _endDate = null;
        OnPropertyChanged(nameof(SelectedBorrowerId));
        OnPropertyChanged(nameof(SelectedTransactionType));
        OnPropertyChanged(nameof(SearchTerm));
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(EndDate));
        _ = LoadAsync();
    }
}

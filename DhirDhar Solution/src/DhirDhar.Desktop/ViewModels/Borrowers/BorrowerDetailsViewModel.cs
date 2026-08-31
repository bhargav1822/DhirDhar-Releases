using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Ledger;
using DhirDhar.Application.Ledger.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.QrCode;
using DhirDhar.Application.Transactions;
using DhirDhar.Application.Transactions.Models;
using DhirDhar.Desktop.Navigation;
using DhirDhar.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DhirDhar.Desktop.ViewModels.Borrowers;

public sealed record BorrowerStatementRow(
    Guid? Id,
    DateTime Date,
    string FormattedDate,
    decimal Withdrawal,
    string DisplayWithdrawal,
    decimal Deposit,
    string DisplayDeposit,
    decimal InterestAmount,
    string DisplayInterest,
    string Percent,
    decimal RunningBalance,
    string FormattedRunningBalance,
    string RowType)
{
    public string DisplayPercent => !string.IsNullOrWhiteSpace(Percent) ? Percent : "—";
}

public sealed class BorrowerDetailsViewModel : ViewModelBase
{
    private readonly IBorrowerService _borrowerService;
    private readonly ILedgerService _ledgerService;
    private readonly ITransactionService _transactionService;
    private readonly DhirDhar.Application.Interest.IInterestCalculationService _interestService;
    private readonly ILocalizationService _localizationService;
    private readonly INavigationService _navigationService;
    private readonly IQrCodeService? _qrCodeService;
    private readonly Services.IImageCacheService? _imageCacheService;
    private readonly ITranslationService? _translationService;
    private readonly Services.IInputLanguageService? _inputLanguageService;
    private readonly ILogger<BorrowerDetailsViewModel> _logger;

    private Guid _borrowerId;
    private BorrowerSummary? _borrower;
    private LedgerSummary? _ledgerSummary;
    private DhirDhar.Domain.Interest.InterestCalculationResult? _interestResult;

    private ImageSource? _borrowerPhotoPreview;
    private ImageSource? _ornamentPhotoPreview;
    private ImageSource? _qrCodeImage;

    private ObservableCollection<BorrowerStatementRow> _allStatementRows = new();
    private ObservableCollection<BorrowerStatementRow> _displayStatementRows = new();

    private string _selectedTypeFilter = "All";

    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage = string.Empty;

    public BorrowerDetailsViewModel(
        IBorrowerService borrowerService,
        ILedgerService ledgerService,
        ITransactionService transactionService,
        DhirDhar.Application.Interest.IInterestCalculationService interestService,
        ILocalizationService localizationService,
        INavigationService navigationService,
        ILogger<BorrowerDetailsViewModel> logger,
        ITranslationService? translationService = null,
        Services.IInputLanguageService? inputLanguageService = null,
        IQrCodeService? qrCodeService = null,
        Services.IImageCacheService? imageCacheService = null)
    {
        _borrowerService = borrowerService;
        _ledgerService = ledgerService;
        _transactionService = transactionService;
        _interestService = interestService;
        _localizationService = localizationService;
        _navigationService = navigationService;
        _logger = logger;
        _translationService = translationService;
        _inputLanguageService = inputLanguageService;
        _qrCodeService = qrCodeService ?? App.ServiceProvider?.GetService<IQrCodeService>();
        _imageCacheService = imageCacheService ?? App.ServiceProvider?.GetService<Services.IImageCacheService>();

        LoadCommand = new RelayCommand(async () => await LoadAsync());
        RetryCommand = new RelayCommand(async () => await LoadAsync());
        BackCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Dashboard));

        EditBorrowerCommand = new RelayCommand(OpenBorrowerForEdit);
        NewTransactionCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Transactions, BorrowerId));
        ReceivePaymentCommand = new RelayCommand(OpenReceiveAmountDialog);
        ReceiveAmountCommand = new RelayCommand(OpenReceiveAmountDialog);
        GiveAmountCommand = new RelayCommand(OpenGiveAmountDialog);
        SaveTransactionCommand = new RelayCommand(async () => await SaveTransactionAsync());
        CancelTransactionCommand = new RelayCommand(CloseTransactionDialog);

        OpenCloseAccountDialogCommand = new RelayCommand(OpenCloseAccountDialog);
        CloseAccountCommand = OpenCloseAccountDialogCommand;
        CancelCloseAccountCommand = new RelayCommand(CancelCloseAccount);
        ConfirmCloseAccountCommand = new RelayCommand(async () => await ConfirmCloseAccountAsync());
        ReopenAccountCommand = new RelayCommand(async () => await ReopenAccountAsync());

        SaveQrCommand = new RelayCommand(async () => await SaveQrAsync());
        PrintQrCommand = new RelayCommand(async () => await PrintQrAsync());
        PrintStatementCommand = new RelayCommand(async () => await PrintStatementAsync());
        PrintReceiptCommand = new RelayCommand(async () => await PrintLatestTransactionReceiptAsync());

        FilterAllCommand = new RelayCommand(() => SetTypeFilter("All"));
        FilterGivenCommand = new RelayCommand(() => SetTypeFilter("Given"));
        FilterReceivedCommand = new RelayCommand(() => SetTypeFilter("Received"));
        FilterInterestCommand = new RelayCommand(() => SetTypeFilter("Interest"));

        ActivateCommand = new RelayCommand(async () => await ChangeStatusAsync(Domain.Enums.BorrowerStatus.Active));
        DeactivateCommand = new RelayCommand(async () => await ChangeStatusAsync(Domain.Enums.BorrowerStatus.Inactive));
        ArchiveCommand = new RelayCommand(async () => await ChangeStatusAsync(Domain.Enums.BorrowerStatus.Archived));

        _localizationService.LanguageChanged += (s, e) =>
        {
            OnPropertyChanged(string.Empty);
            _ = LoadAsync();
        };
    }

    public RelayCommand SaveQrCommand { get; }
    public RelayCommand PrintQrCommand { get; }
    public RelayCommand PrintStatementCommand { get; }
    public RelayCommand PrintReceiptCommand { get; }

    internal Func<BorrowerEditViewModel>? BorrowerEditViewModelFactory { get; set; }
    internal Action<BorrowerEditViewModel>? BorrowerEditNavigationRequested { get; set; }

    public Guid BorrowerId
    {
        get => _borrowerId;
        set => SetProperty(ref _borrowerId, value);
    }

    public BorrowerSummary? Borrower
    {
        get => _borrower;
        private set
        {
            if (SetProperty(ref _borrower, value))
            {
                OnPropertyChanged(nameof(HasBorrowerPhoto));
                OnPropertyChanged(nameof(HasOrnamentPhoto));
                OnPropertyChanged(nameof(BorrowerName));
                OnPropertyChanged(nameof(FullName));
                OnPropertyChanged(nameof(BorrowerNumber));
                OnPropertyChanged(nameof(FatherName));
                OnPropertyChanged(nameof(Surname));
                OnPropertyChanged(nameof(Village));
                OnPropertyChanged(nameof(BorrowerPhone));
                OnPropertyChanged(nameof(BorrowerStatus));
                OnPropertyChanged(nameof(BorrowerStatusBrushValue));
                OnPropertyChanged(nameof(DisplayLoanType));
                OnPropertyChanged(nameof(DisplayLoanAmount));
                OnPropertyChanged(nameof(DisplayLoanDate));
                OnPropertyChanged(nameof(MaskedAadharNumber));
                OnPropertyChanged(nameof(CurrentPrincipal));
                OnPropertyChanged(nameof(MonthlyInterestRate));
                OnPropertyChanged(nameof(DisplayMonthlyInterestRate));
                OnPropertyChanged(nameof(MonthlyInterest));
                OnPropertyChanged(nameof(DisplayMonthlyInterest));
                OnPropertyChanged(nameof(BorrowerDuration));
                OnPropertyChanged(nameof(DisplayBorrowerDuration));
                OnPropertyChanged(nameof(DisplayCompletedMonths));
                OnPropertyChanged(nameof(HasOrnamentDetails));
                OnPropertyChanged(nameof(IsOrnamentLoanType));
                OnPropertyChanged(nameof(IsOrnamentSectionVisible));
                OnPropertyChanged(nameof(DisplayOrnamentType));
                OnPropertyChanged(nameof(DisplayOrnamentWeight));
                OnPropertyChanged(nameof(IsClosed));
                OnPropertyChanged(nameof(AccountClosedDate));
                OnPropertyChanged(nameof(AccountClosedDateText));
                OnPropertyChanged(nameof(AccountClosedSeparatorText));
            }
        }
    }

    public LedgerSummary? LedgerSummary
    {
        get => _ledgerSummary;
        private set => SetProperty(ref _ledgerSummary, value);
    }

    public ImageSource? BorrowerPhotoPreview
    {
        get => _borrowerPhotoPreview;
        private set => SetProperty(ref _borrowerPhotoPreview, value);
    }

    public ImageSource? OrnamentPhotoPreview
    {
        get => _ornamentPhotoPreview;
        private set => SetProperty(ref _ornamentPhotoPreview, value);
    }

    public ImageSource? QrCodeImage
    {
        get => _qrCodeImage;
        private set => SetProperty(ref _qrCodeImage, value);
    }

    public ObservableCollection<BorrowerStatementRow> DisplayStatementRows
    {
        get => _displayStatementRows;
        private set => SetProperty(ref _displayStatementRows, value);
    }

    public bool IsAllSelected => string.Equals(SelectedTypeFilter, "All", StringComparison.OrdinalIgnoreCase);
    public bool IsGivenSelected => string.Equals(SelectedTypeFilter, "Given", StringComparison.OrdinalIgnoreCase);
    public bool IsReceivedSelected => string.Equals(SelectedTypeFilter, "Received", StringComparison.OrdinalIgnoreCase);
    public bool IsInterestSelected => string.Equals(SelectedTypeFilter, "Interest", StringComparison.OrdinalIgnoreCase);

    public string SelectedTypeFilter
    {
        get => _selectedTypeFilter;
        set
        {
            if (SetProperty(ref _selectedTypeFilter, value))
            {
                OnPropertyChanged(nameof(IsAllSelected));
                OnPropertyChanged(nameof(IsGivenSelected));
                OnPropertyChanged(nameof(IsReceivedSelected));
                OnPropertyChanged(nameof(IsInterestSelected));
                ApplyFilters();
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

    public bool HasStatementRows => DisplayStatementRows.Count > 0;
    public bool HasNoStatementRows => !HasStatementRows && !IsLoading;

    public bool HasBorrowerPhoto => !string.IsNullOrWhiteSpace(Borrower?.BorrowerPhotoPath) && File.Exists(Borrower?.BorrowerPhotoPath);
    public bool HasOrnamentPhoto => !string.IsNullOrWhiteSpace(Borrower?.OrnamentPhotoPath) && File.Exists(Borrower?.OrnamentPhotoPath);

    public string BorrowerName => Borrower?.Name is null ? string.Empty : (_translationService?.Translate(Borrower.Name, _localizationService.CurrentLanguage) ?? Borrower.Name);

    public string FullName
    {
        get
        {
            if (Borrower is null) return string.Empty;
            var parts = new List<string>();
            var targetLang = _localizationService.CurrentLanguage;
            if (!string.IsNullOrWhiteSpace(Borrower.Name)) parts.Add((_translationService?.Translate(Borrower.Name.Trim(), targetLang) ?? Borrower.Name.Trim()));
            if (!string.IsNullOrWhiteSpace(Borrower.FatherName)) parts.Add((_translationService?.Translate(Borrower.FatherName.Trim(), targetLang) ?? Borrower.FatherName.Trim()));
            if (!string.IsNullOrWhiteSpace(Borrower.Surname)) parts.Add((_translationService?.Translate(Borrower.Surname.Trim(), targetLang) ?? Borrower.Surname.Trim()));
            return parts.Count > 0 ? string.Join(" ", parts) : (_translationService?.Translate(Borrower.Name ?? string.Empty, targetLang) ?? (Borrower.Name ?? string.Empty));
        }
    }

    public string BorrowerNumber => string.IsNullOrWhiteSpace(Borrower?.BorrowerNumber) ? "N/A" : LocalizeDigits(Borrower.BorrowerNumber);
    public string FatherName => string.IsNullOrWhiteSpace(Borrower?.FatherName) ? "N/A" : (_translationService?.Translate(Borrower.FatherName, _localizationService.CurrentLanguage) ?? Borrower.FatherName);
    public string Surname => string.IsNullOrWhiteSpace(Borrower?.Surname) ? "N/A" : (_translationService?.Translate(Borrower.Surname, _localizationService.CurrentLanguage) ?? Borrower.Surname);
    public string Village => string.IsNullOrWhiteSpace(Borrower?.Village) ? "N/A" : (_translationService?.Translate(Borrower.Village, _localizationService.CurrentLanguage) ?? Borrower.Village);
    public string BorrowerPhone => string.IsNullOrWhiteSpace(Borrower?.Contact) ? "N/A" : LocalizeDigits(Borrower.Contact);
    public string BorrowerStatus => string.IsNullOrWhiteSpace(Borrower?.Status) ? string.Empty : _localizationService.GetString(Borrower.Status);
    public string BorrowerStatusBrushValue => Borrower?.Status ?? string.Empty;

    public string DisplayLoanType => string.IsNullOrWhiteSpace(Borrower?.LoanType) ? _localizationService.GetString("Cash") : (_translationService?.Translate(Borrower.LoanType, _localizationService.CurrentLanguage) ?? _localizationService.GetString(Borrower.LoanType));

    public string DisplayLoanAmount
    {
        get
        {
            if (Borrower?.LoanAmount.HasValue == true)
            {
                return LCur(Borrower.LoanAmount.Value);
            }
            return _localizationService.GetString("NotSpecified");
        }
    }

    public string DisplayLoanDate
    {
        get
        {
            if (Borrower?.LoanDate.HasValue == true)
            {
                return LDate(Borrower.LoanDate.Value, "dd/MM/yyyy");
            }
            return _localizationService.GetString("NotSpecified");
        }
    }

    public bool IsClosed => string.Equals(Borrower?.Status, "Closed", StringComparison.OrdinalIgnoreCase) || Borrower?.Status == Domain.Enums.BorrowerStatus.Closed.ToString();

    public DateTime? AccountClosedDate => Borrower?.ClosedDate ?? _interestResult?.ClosedDate;

    public string AccountClosedDateText => LDate(AccountClosedDate, "dd-MM-yyyy");

    public string AccountClosedSeparatorText
    {
        get
        {
            if (!IsClosed)
            {
                return string.Empty;
            }

            DateTime? closedDate = AccountClosedDate;
            if (closedDate.HasValue)
            {
                var template = _localizationService.GetString("AccountClosedOn");
                var formatted = string.Format(template, LDate(closedDate.Value));
                return $"---------- {formatted} ----------";
            }

            return $"---------- {_localizationService.GetString("AccountClosed")} ----------";
        }
    }

    public string EditBorrowerLabel => _localizationService.GetString("EditBorrower");
    public string ReceiveAmountLabel => _localizationService.GetString("ReceiveAmount");
    public string GiveAmountLabel => _localizationService.GetString("GiveAmount");
    public string CloseAccountLabel => _localizationService.GetString("CloseAccount");
    public string ReopenAccountLabel => _localizationService.GetString("ReopenAccount");
    public string BackLabel => _localizationService.GetString("Back");
    public string RetryLabel => _localizationService.GetString("Retry");
    public string NoTransactionsLabel => _localizationService.GetString("NoTransactions");

    public string BorrowerProfileLabel => _localizationService.GetString("BorrowerProfile");
    public string FullNameLabel => _localizationService.GetString("FullName");
    public string NameLabel => _localizationService.GetString("FullName");
    public string FatherNameLabel => string.Empty;
    public string SurnameLabel => string.Empty;
    public string MobileNumberLabel => _localizationService.GetString("MobileNumber");
    public string AadharNumberLabel => _localizationService.GetString("AadharNumber");
    public string BorrowerNumberLabel => _localizationService.GetString("BorrowerNumber");
    public string VillageLabel => _localizationService.GetString("Village");
    public string AccountStatusLabel => _localizationService.GetString("StatusColumn");
    public string BorrowerPhotoLabel => _localizationService.GetString("BorrowerPhoto");
    public string NoBorrowerPhotoLabel => _localizationService.GetString("NoBorrowerPhoto");
    public string AccountQrLabel => _localizationService.GetString("AccountQr");
    public string SaveQrLabel => _localizationService.GetString("SaveQr");
    public string PrintQrLabel => _localizationService.GetString("PrintQr");
    public string PrintStatementLabel => _localizationService.GetString("PrintStatement");
    public string PrintReceiptLabel => _localizationService.GetString("PrintReceipt");
    public string FormattedBorrowerNumber => _borrower?.FormattedBorrowerNumber ?? (!string.IsNullOrWhiteSpace(_borrower?.BorrowerNumber) ? (_borrower.BorrowerNumber.StartsWith("#") ? _borrower.BorrowerNumber : $"#{_borrower.BorrowerNumber}") : string.Empty);

    public string LoanSecurityDetailsLabel => _localizationService.GetString("LoanType");
    public string LoanTypeLabel => _localizationService.GetString("LoanType");
    public string LoanAmountLabel => _localizationService.GetString("LoanAmount");
    public string LoanDateLabel => _localizationService.GetString("LoanDate");
    public string InterestRateLabel => _localizationService.GetString("InterestRate");
    public string BorrowerDurationLabel => _localizationService.GetString("BorrowerDuration");
    public string MonthlyInterestLabel => _localizationService.GetString("MonthlyInterest");
    public string OrnamentTypeLabel => _localizationService.GetString("OrnamentType");
    public string OrnamentWeightLabel => _localizationService.GetString("OrnamentWeight");
    public string OrnamentPhotoLabel => _localizationService.GetString("OrnamentPhoto");
    public string NoOrnamentPhotoLabel => _localizationService.GetString("NoOrnamentPhoto");

    public string TotalOutstandingBalanceLabel => _localizationService.GetString("TotalOutstanding");
    public string PrincipalBalanceLabel => _localizationService.GetString("CurrentBalance");
    public string AccruedInterestLabel => _localizationService.GetString("TotalAccrued");

    public string TotalGivenLabel => _localizationService.GetString("TotalWithdrawals");
    public string TotalReceivedLabel => _localizationService.GetString("TotalDeposits");
    public string InterestAccruedLabel => _localizationService.GetString("InterestEarned");

    public string CompleteHistoryLabel => _localizationService.GetString("RecentTransactions");
    public string HistorySubtitleLabel => _localizationService.GetString("TransactionsSubtitle");
    public string AllLabel => _localizationService.GetString("All");
    public string GivenLabel => _localizationService.GetString("Withdrawal");
    public string ReceivedLabel => _localizationService.GetString("Deposit");
    public string InterestLabel => _localizationService.GetString("Interest");

    public string DateHeaderLabel => _localizationService.GetString("Date");
    public string WithdrawalHeaderLabel => _localizationService.GetString("Withdrawal");
    public string DepositHeaderLabel => _localizationService.GetString("Deposit");
    public string InterestHeaderLabel => _localizationService.GetString("InterestAmount");
    public string PercentHeaderLabel => _localizationService.GetString("Percent");
    public string RunningBalanceHeaderLabel => _localizationService.GetString("RunningBalance");

    public string CancelLabel => _localizationService.GetString("Cancel");
    public string SaveTransactionLabel => _localizationService.GetString("SaveBorrower");
    public string TransactionAmountFieldLabel => _localizationService.GetString("TransactionAmountField");
    public string EnterAmountPlaceholderLabel => _localizationService.GetString("EnterAmount");
    public string TransactionDateFieldLabel => _localizationService.GetString("TransactionDateField");
    public string ClosingDateFieldLabel => _localizationService.GetString("ClosingDateField");
    public string TransactionDescriptionFieldLabel => _localizationService.GetString("TransactionDescriptionField");
    public string TransactionDescriptionPlaceholderLabel => _localizationService.GetString("TransactionDescriptionPlaceholder");
    public string ClosingAmountFieldLabel => _localizationService.GetString("ClosingAmountField");
    public string ClosingAmountLabel => _localizationService.GetString("ClosingAmount");
    public string CloseAccountConfirmLabel => _localizationService.GetString("CloseAccountConfirm");
    public string CloseAccountInfoLabel => _localizationService.GetString("CloseAccountInfo");

    public string BorrowerDuration
    {
        get
        {
            DateTime? startDate = Borrower?.LoanDate;
            if (!startDate.HasValue)
            {
                return FormatDuration(0, 0, 0);
            }

            DateTime endDate;
            if (IsClosed)
            {
                endDate = Borrower?.ClosedDate ?? _interestResult?.ClosedDate ?? _interestResult?.CalculationEndDate ?? DateTime.Today;
            }
            else
            {
                endDate = _interestResult?.CalculationEndDate ?? DateTime.Today;
            }

            if (startDate.Value >= endDate)
            {
                return FormatDuration(0, 0, 0);
            }

            int years = endDate.Year - startDate.Value.Year;
            int months = endDate.Month - startDate.Value.Month;
            int days = endDate.Day - startDate.Value.Day;

            if (days < 0)
            {
                months--;
                DateTime prevMonthDate = endDate.AddMonths(-1);
                int daysInPrevMonth = DateTime.DaysInMonth(prevMonthDate.Year, prevMonthDate.Month);
                days += daysInPrevMonth;
            }

            if (months < 0)
            {
                years--;
                months += 12;
            }

            return FormatDuration(days, months, years);
        }
    }

    private string FormatDuration(int days, int months, int years)
    {
        var normLang = ScriptTranslator.NormalizeLanguageCode(_localizationService.CurrentLanguage);
        var (dayUnit, monthUnit, yearUnit) = normLang switch
        {
            "gu" => ("દિ", "મ", "વ"),
            "hi" => ("दि", "मा", "व"),
            "mr" => ("दि", "म", "व"),
            "bn" => ("দি", "মা", "ব"),
            "pa" => ("ਦਿ", "ਮ", "ਸ"),
            "ta" => ("நா", "மா", "ஆ"),
            "te" => ("రో", "నె", "సం"),
            "kn" => ("ದಿ", "ತಿ", "ವ"),
            "ml" => ("ദി", "മാ", "വ"),
            "or" => ("ଦି", "ମା", "ବ"),
            "as" => ("দি", "মা", "ব"),
            _ => ("D", "M", "Y")
        };

        var rawStr = $"{days:D2}{dayUnit} {months:D2}{monthUnit} {years:D2}{yearUnit}";
        return LocalizeDigits(rawStr);
    }

    public string DisplayBorrowerDuration => BorrowerDuration;
    public int CompletedMonths => _interestResult?.CompletedMonths ?? 0;
    public string DisplayCompletedMonths => DisplayBorrowerDuration;

    public decimal CurrentPrincipal => _interestResult?.ClosingPrincipal ?? (Borrower?.LoanAmount ?? 0m);

    public decimal MonthlyInterestRate =>
        (Borrower?.InterestRate.HasValue == true && Borrower.InterestRate.Value > 0m)
            ? Borrower.InterestRate.Value
            : (_interestResult?.MonthlyInterestRate ?? 3.0m);
    public string DisplayMonthlyInterestRate => $"{LPct(MonthlyInterestRate)} {_localizationService.GetString("PerMonth")}";

    public decimal MonthlyInterest => DhirDhar.Domain.Common.FinancialRounding.RoundInterest(CurrentPrincipal * (MonthlyInterestRate / 100m));
    public string DisplayMonthlyInterest => $"{LCur(MonthlyInterest)} {_localizationService.GetString("PerMonthShort")}";

    public bool HasOrnamentDetails =>
        !string.IsNullOrWhiteSpace(Borrower?.OrnamentType) ||
        (Borrower?.OrnamentWeight.HasValue == true && Borrower.OrnamentWeight.Value > 0m) ||
        HasOrnamentPhoto;

    public bool IsOrnamentLoanType =>
        string.Equals(Borrower?.LoanType, "Gold", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Borrower?.LoanType, "Silver", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(DisplayLoanType, "Gold", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(DisplayLoanType, "Silver", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(DisplayLoanType, "સોનું", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(DisplayLoanType, "ચાંદી", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(DisplayLoanType, "सोना", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(DisplayLoanType, "चांदी", StringComparison.OrdinalIgnoreCase);

    public bool IsOrnamentSectionVisible => HasOrnamentDetails || IsOrnamentLoanType;

    public string DisplayOrnamentType
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Borrower?.OrnamentType))
            {
                var targetLang = _localizationService.CurrentLanguage;
                return _translationService?.Translate(Borrower.OrnamentType, targetLang) ?? _localizationService.LocalizeText(Borrower.OrnamentType);
            }

            if (IsOrnamentLoanType)
            {
                return _localizationService.GetString("NotSpecified");
            }

            return _localizationService.GetString("NotApplicable");
        }
    }

    public string DisplayOrnamentWeight
    {
        get
        {
            if (Borrower?.OrnamentWeight.HasValue == true && Borrower.OrnamentWeight.Value > 0m)
            {
                return $"{LNum(Borrower.OrnamentWeight.Value)} {_localizationService.GetString("Grams")}";
            }

            if (IsOrnamentLoanType)
            {
                return _localizationService.GetString("NotSpecified");
            }

            return _localizationService.GetString("NotApplicable");
        }
    }

    public string SubtitleHeader
    {
        get
        {
            var parts = new List<string>();
            var targetLang = _localizationService.CurrentLanguage;
            var father = !string.IsNullOrWhiteSpace(Borrower?.FatherName)
                ? (_translationService?.Translate(Borrower.FatherName, targetLang) ?? Borrower.FatherName)
                : null;
            var surname = !string.IsNullOrWhiteSpace(Borrower?.Surname)
                ? (_translationService?.Translate(Borrower.Surname, targetLang) ?? Borrower.Surname)
                : null;
            var village = !string.IsNullOrWhiteSpace(Borrower?.Village)
                ? (_translationService?.Translate(Borrower.Village, targetLang) ?? Borrower.Village)
                : null;

            if (!string.IsNullOrWhiteSpace(father))
            {
                var template = _localizationService.GetString("SonOf");
                parts.Add(template.Contains("{0}") ? string.Format(template, father) : $"{template} {father}");
            }
            if (!string.IsNullOrWhiteSpace(surname)) parts.Add(surname);
            if (!string.IsNullOrWhiteSpace(village)) parts.Add(village);
            return parts.Count > 0 ? string.Join(" • ", parts) : _localizationService.GetString("BorrowerDetailsSubtitle");
        }
    }

    public string MaskedAadharNumber
    {
        get
        {
            var raw = Borrower?.AadharNumber;
            if (string.IsNullOrWhiteSpace(raw)) return "N/A";
            var ascii = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(raw);
            var digitsOnly = new string(ascii.Where(char.IsDigit).ToArray());
            if (digitsOnly.Length >= 4)
            {
                return $"XXXX XXXX {LocalizeDigits(digitsOnly[^4..])}";
            }
            return "XXXX XXXX XXXX";
        }
    }

    public decimal TotalGiven => (LedgerSummary?.OpeningBalance ?? Borrower?.LoanAmount ?? 0m) + (LedgerSummary?.TotalWithdrawals ?? Borrower?.TotalWithdrawals ?? 0m);
    public decimal TotalReceived => LedgerSummary?.TotalDeposits ?? Borrower?.TotalDeposits ?? 0m;
    public decimal TotalWithdrawals => TotalGiven;
    public decimal TotalInterest => (IsClosed && Borrower?.ClosedAccruedInterest.HasValue == true) ? Borrower.ClosedAccruedInterest.Value : (LedgerSummary?.TotalInterest ?? _interestResult?.TotalInterest ?? 0m);
    public decimal OutstandingPrincipal => Math.Max(0m, TotalGiven - TotalReceived);
    public decimal OutstandingInterest => (IsClosed && Borrower?.ClosedAccruedInterest.HasValue == true) ? Borrower.ClosedAccruedInterest.Value : TotalInterest;
    public decimal TotalOutstanding => (IsClosed && Borrower?.ClosingAmount.HasValue == true) ? Borrower.ClosingAmount.Value : (OutstandingPrincipal + OutstandingInterest);

    public string FormattedTotalGiven => LCur(TotalGiven);
    public string FormattedTotalReceived => LCur(TotalReceived);
    public string FormattedTotalWithdrawals => LCur(TotalWithdrawals);
    public string FormattedTotalInterest => LCur(TotalInterest);
    public string FormattedOutstandingPrincipal => LCur(OutstandingPrincipal);
    public string FormattedOutstandingInterest => LCur(OutstandingInterest);
    public string FormattedTotalOutstanding => LCur(TotalOutstanding);

    public string FormattedCreatedAt => LDate(Borrower?.EntryDate, "dd/MM/yyyy");
    public string FormattedUpdatedAt => Borrower?.LastTransactionDate.HasValue == true ? LDate(Borrower.LastTransactionDate.Value, "dd/MM/yyyy") : _localizationService.GetString("NotApplicable");

    private bool _isTransactionDialogOpen;
    private string _transactionDialogTitle = string.Empty;
    private string _transactionType = "Deposit";
    private string _transactionAmountText = string.Empty;
    private DateTimeOffset? _transactionDate = DateTimeOffset.Now.Date;
    private string _transactionDescription = string.Empty;
    private string _transactionValidationError = string.Empty;
    private bool _isSavingTransaction;

    public bool IsTransactionDialogOpen
    {
        get => _isTransactionDialogOpen;
        set => SetProperty(ref _isTransactionDialogOpen, value);
    }

    public string TransactionDialogTitle
    {
        get => _transactionDialogTitle;
        private set => SetProperty(ref _transactionDialogTitle, value);
    }

    public string TransactionType
    {
        get => _transactionType;
        private set => SetProperty(ref _transactionType, value);
    }

    public string TransactionAmountText
    {
        get => _transactionAmountText;
        set => SetProperty(ref _transactionAmountText, value);
    }

    public DateTimeOffset? TransactionDate
    {
        get => _transactionDate;
        set => SetProperty(ref _transactionDate, value);
    }

    private string ProcessIndicTextInput(string input)
    {
        // Single authoritative engine is IndicInput; ViewModel stores UI-provided text as-is.
        return input ?? string.Empty;
    }

    public string TransactionDescription
    {
        get => _transactionDescription;
        set => SetProperty(ref _transactionDescription, value ?? string.Empty);
    }

    public string TransactionValidationError
    {
        get => _transactionValidationError;
        private set
        {
            if (SetProperty(ref _transactionValidationError, value))
            {
                OnPropertyChanged(nameof(HasTransactionValidationError));
            }
        }
    }

    public bool HasTransactionValidationError => !string.IsNullOrWhiteSpace(TransactionValidationError);

    public bool IsSavingTransaction
    {
        get => _isSavingTransaction;
        private set => SetProperty(ref _isSavingTransaction, value);
    }

    private bool _isCloseAccountDialogOpen;
    private DateTimeOffset? _closeAccountDate = DateTimeOffset.Now.Date;
    private decimal _closingAmount;
    private decimal _closingAccruedInterest;
    private decimal _closingOutstandingPrincipal;
    private bool _isCalculatingClosingAmount;
    private string _closeAccountValidationError = string.Empty;
    private bool _isClosingAccount;

    public bool IsCloseAccountDialogOpen
    {
        get => _isCloseAccountDialogOpen;
        set => SetProperty(ref _isCloseAccountDialogOpen, value);
    }

    public DateTimeOffset? CloseAccountDate
    {
        get => _closeAccountDate;
        set
        {
            if (SetProperty(ref _closeAccountDate, value))
            {
                _ = UpdateClosingAmountAsync();
            }
        }
    }

    public decimal ClosingAmount
    {
        get => _closingAmount;
        private set
        {
            if (SetProperty(ref _closingAmount, value))
            {
                OnPropertyChanged(nameof(DisplayClosingAmount));
            }
        }
    }

    public string DisplayClosingAmount => LCur(ClosingAmount);

    public bool IsCalculatingClosingAmount
    {
        get => _isCalculatingClosingAmount;
        private set => SetProperty(ref _isCalculatingClosingAmount, value);
    }

    public string CloseAccountValidationError
    {
        get => _closeAccountValidationError;
        private set
        {
            if (SetProperty(ref _closeAccountValidationError, value))
            {
                OnPropertyChanged(nameof(HasCloseAccountValidationError));
            }
        }
    }

    public bool HasCloseAccountValidationError => !string.IsNullOrWhiteSpace(CloseAccountValidationError);

    public bool IsClosingAccount
    {
        get => _isClosingAccount;
        private set => SetProperty(ref _isClosingAccount, value);
    }

    public void OpenCloseAccountDialog()
    {
        if (IsClosed) return;
        CloseAccountDate = DateTimeOffset.Now.Date;
        CloseAccountValidationError = string.Empty;
        _ = UpdateClosingAmountAsync();
        IsCloseAccountDialogOpen = true;
    }

    private async Task UpdateClosingAmountAsync()
    {
        if (BorrowerId == Guid.Empty || !CloseAccountDate.HasValue)
        {
            ClosingAmount = 0m;
            _closingAccruedInterest = 0m;
            _closingOutstandingPrincipal = 0m;
            return;
        }

        try
        {
            IsCalculatingClosingAmount = true;
            var targetDate = CloseAccountDate.Value.DateTime.Date;
            var calculation = await _interestService.CalculateAsync(BorrowerId, targetDate);
            _closingAccruedInterest = calculation.TotalInterest;
            _closingOutstandingPrincipal = calculation.ClosingPrincipal;
            ClosingAmount = calculation.TotalOutstanding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate closing amount for borrower '{BorrowerId}'", BorrowerId);
        }
        finally
        {
            IsCalculatingClosingAmount = false;
        }
    }

    public void CancelCloseAccount()
    {
        IsCloseAccountDialogOpen = false;
        CloseAccountValidationError = string.Empty;
    }

    public async Task ConfirmCloseAccountAsync()
    {
        if (BorrowerId == Guid.Empty || IsClosed) return;

        if (!CloseAccountDate.HasValue)
        {
            CloseAccountValidationError = _localizationService.GetString("InvalidTransactionDate");
            return;
        }

        IsClosingAccount = true;
        CloseAccountValidationError = string.Empty;

        try
        {
            var targetDate = CloseAccountDate.Value.DateTime.Date;
            var calculation = await _interestService.CalculateAsync(BorrowerId, targetDate);
            var finalClosingAmount = calculation.TotalOutstanding;
            var finalInterest = calculation.TotalInterest;

            await _borrowerService.CloseAccountAsync(BorrowerId, targetDate, finalClosingAmount, finalInterest);
            IsCloseAccountDialogOpen = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close account for borrower '{BorrowerId}'", BorrowerId);
            CloseAccountValidationError = _localizationService.GetString("CloseAccountFailed");
        }
        finally
        {
            IsClosingAccount = false;
        }
    }

    public RelayCommand LoadCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand BackCommand { get; }

    public RelayCommand EditBorrowerCommand { get; }
    public RelayCommand NewTransactionCommand { get; }
    public RelayCommand ReceivePaymentCommand { get; }
    public RelayCommand ReceiveAmountCommand { get; }
    public RelayCommand GiveAmountCommand { get; }
    public RelayCommand SaveTransactionCommand { get; }
    public RelayCommand CancelTransactionCommand { get; }

    public RelayCommand OpenCloseAccountDialogCommand { get; }
    public RelayCommand CloseAccountCommand { get; }
    public RelayCommand CancelCloseAccountCommand { get; }
    public RelayCommand ConfirmCloseAccountCommand { get; }
    public RelayCommand ReopenAccountCommand { get; }

    public RelayCommand FilterAllCommand { get; }
    public RelayCommand FilterGivenCommand { get; }
    public RelayCommand FilterReceivedCommand { get; }
    public RelayCommand FilterInterestCommand { get; }

    public RelayCommand ActivateCommand { get; }
    public RelayCommand DeactivateCommand { get; }
    public RelayCommand ArchiveCommand { get; }

    public void OpenReceiveAmountDialog()
    {
        TransactionDialogTitle = _localizationService.GetString("ReceiveAmount");
        TransactionType = "Deposit";
        TransactionAmountText = string.Empty;
        TransactionDate = DateTimeOffset.Now.Date;
        TransactionDescription = string.Empty;
        TransactionValidationError = string.Empty;
        IsTransactionDialogOpen = true;
    }

    public void OpenGiveAmountDialog()
    {
        TransactionDialogTitle = _localizationService.GetString("GiveAmount");
        TransactionType = "Withdrawal";
        TransactionAmountText = string.Empty;
        TransactionDate = DateTimeOffset.Now.Date;
        TransactionDescription = string.Empty;
        TransactionValidationError = string.Empty;
        IsTransactionDialogOpen = true;
    }

    public void CloseTransactionDialog()
    {
        IsTransactionDialogOpen = false;
        TransactionValidationError = string.Empty;
        TransactionAmountText = string.Empty;
        TransactionDate = DateTimeOffset.Now.Date;
        TransactionDescription = string.Empty;
    }

    public async Task SaveTransactionAsync()
    {
        TransactionValidationError = string.Empty;

        if (string.IsNullOrWhiteSpace(TransactionAmountText) ||
            !MonetaryAmountParser.TryParse(TransactionAmountText, out var amount) ||
            amount <= 0m)
        {
            TransactionValidationError = _localizationService.GetString("InvalidAmountError");
            return;
        }

        if (!TransactionDate.HasValue)
        {
            TransactionValidationError = _localizationService.GetString("InvalidTransactionDate");
            return;
        }

        if (BorrowerId == Guid.Empty)
        {
            TransactionValidationError = _localizationService.GetString("InvalidBorrowerSelected");
            return;
        }

        IsSavingTransaction = true;
        try
        {
            var type = string.Equals(TransactionType, "Deposit", StringComparison.OrdinalIgnoreCase)
                ? Domain.Enums.TransactionType.Deposit
                : Domain.Enums.TransactionType.Withdrawal;

            var defaultDesc = type == Domain.Enums.TransactionType.Deposit ? "Received Amount" : "Given Amount";
            var description = string.IsNullOrWhiteSpace(TransactionDescription) ? defaultDesc : TransactionDescription.Trim();

            var request = new CreateTransactionRequest(
                BorrowerId,
                type,
                amount,
                TransactionDate.Value.DateTime,
                description);

            await _transactionService.CreateAsync(request);
            IsTransactionDialogOpen = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save transaction for borrower '{BorrowerId}'", BorrowerId);
            TransactionValidationError = _localizationService.GetString("TransactionSaveFailed");
        }
        finally
        {
            IsSavingTransaction = false;
        }
    }

    public async Task LoadAsync()
    {
        if (BorrowerId == Guid.Empty) return;

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var borrowerTask = _borrowerService.GetByIdAsync(BorrowerId);
            var ledgerTask = _ledgerService.GetSummaryAsync(BorrowerId);
            var interestTask = _interestService.CalculateAsync(BorrowerId, DateTime.Today);
            var filterRequest = new TransactionFilterRequest(
                BorrowerId,
                TransactionTypeFilter.All,
                null,
                null,
                null,
                1,
                1000);
            var txnTask = _transactionService.GetListAsync(filterRequest);
            await Task.WhenAll(borrowerTask, ledgerTask, interestTask, txnTask);

            var borrower = await borrowerTask;
            if (borrower is null)
            {
                HasError = true;
                ErrorMessage = _localizationService.GetString("BorrowerNotFound");
                return;
            }

            Borrower = borrower.Localize(_translationService, _localizationService.CurrentLanguage);
            LedgerSummary = await ledgerTask;
            _interestResult = await interestTask;
            var txnResult = await txnTask;

            BuildStatementRows(txnResult.Items, _interestResult);

            var firstLoanTxn = txnResult.Items
                .Where(t => string.Equals(t.TransactionType, "Withdrawal", StringComparison.OrdinalIgnoreCase) || string.Equals(t.Description, "Initial Loan Amount", StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.TransactionDate)
                .ThenBy(t => t.CreatedAt)
                .FirstOrDefault();

            ApplyFilters();
            UpdatePhotoPreviews();
            UpdateQrCodePreview();
            NotifyAllProperties();

            _logger.LogInformation("Loaded BorrowerDetailsPage for BorrowerId '{Id}' successfully.", BorrowerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load BorrowerDetailsPage for BorrowerId '{Id}'.", BorrowerId);
            HasError = true;
            ErrorMessage = _localizationService.GetString("BorrowerDetailsLoadFailed");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasStatementRows));
            OnPropertyChanged(nameof(HasNoStatementRows));
        }
    }

    private void OpenBorrowerForEdit()
    {
        if (BorrowerEditViewModelFactory != null && BorrowerEditNavigationRequested != null && BorrowerId != Guid.Empty)
        {
            var editVm = BorrowerEditViewModelFactory();
            editVm.SetForEdit(BorrowerId);
            BorrowerEditNavigationRequested(editVm);
        }
        else
        {
            _navigationService.Navigate(NavigationDestination.Borrowers);
        }
    }

    private async void UpdatePhotoPreviews()
    {
        var photoPath = Borrower?.BorrowerPhotoPath;
        var ornamentPath = Borrower?.OrnamentPhotoPath;
        bool hasPhoto = HasBorrowerPhoto && !string.IsNullOrWhiteSpace(photoPath);
        bool hasOrnament = HasOrnamentPhoto && !string.IsNullOrWhiteSpace(ornamentPath);

        var cache = _imageCacheService ?? App.ServiceProvider?.GetService<Services.IImageCacheService>();
        if (cache != null)
        {
            var bPhoto = hasPhoto ? await cache.GetOrCreateFromPathAsync(photoPath, decodePixelWidth: 400) : null;
            var oPhoto = hasOrnament ? await cache.GetOrCreateFromPathAsync(ornamentPath, decodePixelWidth: 400) : null;

            var dispatcher = App.MainDispatcherQueue ?? App.MainWindow?.DispatcherQueue;
            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() =>
                {
                    BorrowerPhotoPreview = bPhoto;
                    OrnamentPhotoPreview = oPhoto;
                });
            }
            else
            {
                BorrowerPhotoPreview = bPhoto;
                OrnamentPhotoPreview = oPhoto;
            }
            return;
        }

        var d = App.MainDispatcherQueue ?? App.MainWindow?.DispatcherQueue;
        if (d is null)
        {
            BorrowerPhotoPreview = hasPhoto ? CreateOptimizedBitmapImage(photoPath) : null;
            OrnamentPhotoPreview = hasOrnament ? CreateOptimizedBitmapImage(ornamentPath) : null;
            return;
        }

        d.TryEnqueue(() =>
        {
            BorrowerPhotoPreview = hasPhoto ? CreateOptimizedBitmapImage(photoPath) : null;
            OrnamentPhotoPreview = hasOrnament ? CreateOptimizedBitmapImage(ornamentPath) : null;
        });
    }

    private async void UpdateQrCodePreview()
    {
        var borrowerNumber = Borrower?.BorrowerNumber;
        if (string.IsNullOrWhiteSpace(borrowerNumber))
        {
            QrCodeImage = null;
            return;
        }

        var qrService = _qrCodeService ?? App.ServiceProvider?.GetService<IQrCodeService>();
        if (qrService == null)
        {
            QrCodeImage = null;
            return;
        }

        try
        {
            var pngBytes = qrService.GeneratePngBytes(borrowerNumber, pixelsPerModule: 8);
            if (pngBytes == null || pngBytes.Length == 0)
            {
                QrCodeImage = null;
                return;
            }

            var cache = _imageCacheService ?? App.ServiceProvider?.GetService<Services.IImageCacheService>();
            BitmapImage? qrBitmap = null;
            if (cache != null)
            {
                qrBitmap = await cache.GetOrCreateFromBytesAsync($"qr_{borrowerNumber}", pngBytes, decodePixelWidth: 240);
            }
            else
            {
                qrBitmap = await CreateBitmapFromPngBytesAsync(pngBytes);
            }

            var dispatcher = App.MainDispatcherQueue ?? App.MainWindow?.DispatcherQueue;
            if (dispatcher == null)
            {
                QrCodeImage = qrBitmap;
                return;
            }

            dispatcher.TryEnqueue(() =>
            {
                QrCodeImage = qrBitmap;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate QR code preview for {BorrowerNumber}", borrowerNumber);
            QrCodeImage = null;
        }
    }

    public async Task SaveQrAsync()
    {
        var borrowerNumber = Borrower?.BorrowerNumber;
        if (string.IsNullOrWhiteSpace(borrowerNumber)) return;

        var qrService = _qrCodeService ?? App.ServiceProvider?.GetService<IQrCodeService>();
        if (qrService == null) return;

        try
        {
            var pngBytes = qrService.GeneratePngBytes(borrowerNumber, pixelsPerModule: 15);
            var safeNumber = borrowerNumber.Trim().Replace('#', '_').Replace('/', '_').Replace('\\', '_');
            var picturesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DhirDhar QR Codes");
            Directory.CreateDirectory(picturesDir);
            var filePath = Path.Combine(picturesDir, $"{safeNumber}_Account_QR.png");

            await File.WriteAllBytesAsync(filePath, pngBytes);

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save QR code for borrower {BorrowerNumber}", borrowerNumber);
        }
    }

    public async Task PrintQrAsync()
    {
        var borrowerNumber = Borrower?.BorrowerNumber;
        if (string.IsNullOrWhiteSpace(borrowerNumber)) return;

        var printService = App.ServiceProvider?.GetService(typeof(DhirDhar.Application.Printing.IPrintService)) as DhirDhar.Application.Printing.IPrintService;
        var settingsService = App.ServiceProvider?.GetService(typeof(DhirDhar.Application.Settings.ISettingsService)) as DhirDhar.Application.Settings.ISettingsService;
        var qrService = _qrCodeService ?? App.ServiceProvider?.GetService(typeof(IQrCodeService)) as IQrCodeService;

        if (printService == null) return;

        try
        {
            var settings = settingsService != null ? await settingsService.GetSettingsAsync() : new DhirDhar.Application.Settings.AppSettingsModel();
            var qrBytes = qrService?.GeneratePngBytes(borrowerNumber, pixelsPerModule: 15);

            var receipt = new DhirDhar.Application.Printing.ReceiptData
            {
                Type = DhirDhar.Application.Printing.ReceiptType.BorrowerQrCode,
                BusinessName = settings.BusinessName,
                Title = "Account QR Code",
                BorrowerName = FullName,
                BorrowerNumber = borrowerNumber,
                Contact = BorrowerPhone,
                Village = Village,
                Address = Village,
                LoanDate = Borrower?.LoanDate ?? Borrower?.EntryDate,
                InitialPrincipal = Borrower?.LoanAmount ?? 0m,
                TotalOutstanding = TotalOutstanding,
                QrCodePayload = qrService?.FormatPayload(borrowerNumber),
                QrCodePngBytes = qrBytes,
                PaperSize = settings.PaperSize,
                CustomPaperWidthMm = settings.CustomPaperWidthMm,
                AutoCut = settings.AutoCutPaper,
                LanguageCode = _localizationService.CurrentLanguage
            };

            await printService.PrintReceiptAsync(receipt, settings.SelectedPrinter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print QR code for borrower {BorrowerNumber}", borrowerNumber);
        }
    }

    public async Task PrintStatementAsync()
    {
        var borrowerNumber = Borrower?.BorrowerNumber;
        if (string.IsNullOrWhiteSpace(borrowerNumber)) return;

        var printService = App.ServiceProvider?.GetService(typeof(DhirDhar.Application.Printing.IPrintService)) as DhirDhar.Application.Printing.IPrintService;
        var settingsService = App.ServiceProvider?.GetService(typeof(DhirDhar.Application.Settings.ISettingsService)) as DhirDhar.Application.Settings.ISettingsService;
        var qrService = _qrCodeService ?? App.ServiceProvider?.GetService(typeof(IQrCodeService)) as IQrCodeService;

        if (printService == null) return;

        try
        {
            var settings = settingsService != null ? await settingsService.GetSettingsAsync() : new DhirDhar.Application.Settings.AppSettingsModel();
            var qrBytes = qrService?.GeneratePngBytes(borrowerNumber, pixelsPerModule: 10);

            var receipt = new DhirDhar.Application.Printing.ReceiptData
            {
                Type = DhirDhar.Application.Printing.ReceiptType.AccountStatement,
                BusinessName = settings.BusinessName,
                Title = "Account Statement",
                BorrowerName = FullName,
                BorrowerNumber = borrowerNumber,
                Contact = BorrowerPhone,
                Village = Village,
                Address = Village,
                LoanDate = Borrower?.LoanDate ?? Borrower?.EntryDate,
                InitialPrincipal = Borrower?.LoanAmount ?? 0m,
                InterestRate = MonthlyInterestRate,
                DisplayDuration = DisplayBorrowerDuration,
                MonthlyInterest = MonthlyInterest,
                OrnamentType = DisplayOrnamentType,
                OrnamentWeight = DisplayOrnamentWeight,
                CurrentPrincipal = OutstandingPrincipal,
                TotalInterest = OutstandingInterest,
                TotalOutstanding = TotalOutstanding,
                TotalDeposits = TotalReceived,
                TotalWithdrawals = TotalGiven,
                QrCodePayload = qrService?.FormatPayload(borrowerNumber),
                QrCodePngBytes = qrBytes,
                PaperSize = settings.PaperSize,
                CustomPaperWidthMm = settings.CustomPaperWidthMm,
                AutoCut = settings.AutoCutPaper,
                LanguageCode = _localizationService.CurrentLanguage
            };

            foreach (var row in _allStatementRows)
            {
                receipt.Items.Add(new DhirDhar.Application.Printing.ReceiptItemRow(
                    row.Date,
                    row.RowType,
                    row.Withdrawal > 0 ? row.Withdrawal : null,
                    row.Deposit > 0 ? row.Deposit : null,
                    row.InterestAmount > 0 ? row.InterestAmount : null,
                    row.RunningBalance,
                    null));
            }

            await printService.PrintReceiptAsync(receipt, settings.SelectedPrinter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print statement for borrower {BorrowerNumber}", borrowerNumber);
        }
    }

    public async Task PrintLatestTransactionReceiptAsync()
    {
        var borrowerNumber = Borrower?.BorrowerNumber;
        if (string.IsNullOrWhiteSpace(borrowerNumber)) return;

        var printService = App.ServiceProvider?.GetService(typeof(DhirDhar.Application.Printing.IPrintService)) as DhirDhar.Application.Printing.IPrintService;
        var settingsService = App.ServiceProvider?.GetService(typeof(DhirDhar.Application.Settings.ISettingsService)) as DhirDhar.Application.Settings.ISettingsService;
        var qrService = _qrCodeService ?? App.ServiceProvider?.GetService(typeof(IQrCodeService)) as IQrCodeService;

        if (printService == null) return;

        try
        {
            var settings = settingsService != null ? await settingsService.GetSettingsAsync() : new DhirDhar.Application.Settings.AppSettingsModel();
            var qrBytes = qrService?.GeneratePngBytes(borrowerNumber, pixelsPerModule: 10);

            var latestRow = _allStatementRows.OrderByDescending(r => r.Date).FirstOrDefault();

            var receipt = new DhirDhar.Application.Printing.ReceiptData
            {
                Type = latestRow?.Deposit > 0 ? DhirDhar.Application.Printing.ReceiptType.ReceiveAmount : DhirDhar.Application.Printing.ReceiptType.Transaction,
                BusinessName = settings.BusinessName,
                Title = latestRow?.Deposit > 0 ? "Deposit Receipt" : "Transaction Receipt",
                BorrowerName = FullName,
                BorrowerNumber = borrowerNumber,
                Contact = BorrowerPhone,
                Village = Village,
                Address = Village,
                LoanDate = Borrower?.LoanDate ?? Borrower?.EntryDate,
                TransactionDate = latestRow?.Date ?? DateTime.Today,
                TransactionType = latestRow?.RowType ?? "Transaction",
                TransactionAmount = latestRow != null ? (latestRow.Deposit > 0 ? latestRow.Deposit : (latestRow.Withdrawal > 0 ? latestRow.Withdrawal : latestRow.InterestAmount)) : 0m,
                CurrentPrincipal = OutstandingPrincipal,
                TotalInterest = OutstandingInterest,
                TotalOutstanding = TotalOutstanding,
                QrCodePayload = qrService?.FormatPayload(borrowerNumber),
                QrCodePngBytes = qrBytes,
                PaperSize = settings.PaperSize,
                CustomPaperWidthMm = settings.CustomPaperWidthMm,
                AutoCut = settings.AutoCutPaper,
                LanguageCode = _localizationService.CurrentLanguage
            };

            await printService.PrintReceiptAsync(receipt, settings.SelectedPrinter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print latest transaction receipt for borrower {BorrowerNumber}", borrowerNumber);
        }
    }

    private static async Task<BitmapImage?> CreateBitmapFromPngBytesAsync(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            var bitmap = new BitmapImage();
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using var writer = new Windows.Storage.Streams.DataWriter(stream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? CreateBitmapFromPngBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            var bitmap = new BitmapImage();
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using var writer = new Windows.Storage.Streams.DataWriter(stream);
            writer.WriteBytes(bytes);
            writer.StoreAsync().GetResults();
            writer.FlushAsync().GetResults();
            writer.DetachStream();
            stream.Seek(0);
            bitmap.SetSource(stream);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? CreateOptimizedBitmapImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            Uri? uri = null;
            if (Uri.TryCreate(path, UriKind.Absolute, out var u) && (u.Scheme == "ms-appx" || u.Scheme == "ms-appdata" || u.Scheme == "file" || u.Scheme == "http" || u.Scheme == "https"))
            {
                uri = u;
            }
            else if (File.Exists(path))
            {
                uri = new Uri(path);
            }

            if (uri == null) return null;

            var bitmap = new BitmapImage();
            bitmap.DecodePixelWidth = 400;
            bitmap.UriSource = uri;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void BuildStatementRows(IReadOnlyList<TransactionSummary>? rawTransactions, DhirDhar.Domain.Interest.InterestCalculationResult? interestResult)
    {
        try
        {
            var rows = new List<BorrowerStatementRow>();

            decimal defaultRate = interestResult?.MonthlyInterestRate ?? 3.0m;
            string defaultRateStr = $"{LocalizeDigits(defaultRate.ToString("0.0"))}%";

            // Check if initial loan transaction is present in rawTransactions
            bool hasInitTxnInList = rawTransactions != null && rawTransactions.Any(t =>
                t != null && string.Equals(t.Description, "Initial Loan Amount", StringComparison.OrdinalIgnoreCase));

            if (!hasInitTxnInList && Borrower?.LoanAmount.HasValue == true && Borrower.LoanAmount.Value > 0m)
            {
                var initDate = Borrower.LoanDate ?? Borrower.EntryDate;
                var initAmount = Borrower.LoanAmount.Value;
                 rows.Add(new BorrowerStatementRow(
                    null,
                    initDate,
                    LDate(initDate),
                    initAmount,
                    LCur(initAmount),
                    0m,
                    LCur(0m),
                    0m,
                    LCur(0m),
                    defaultRateStr,
                    0m,
                    string.Empty,
                    "Withdrawal"));
            }

            if (rawTransactions != null)
            {
                foreach (var txn in rawTransactions)
                {
                    if (txn == null) continue;

                    bool isDeposit = string.Equals(txn.TransactionType, "Deposit", StringComparison.OrdinalIgnoreCase);
                     string formattedDate = LDate(txn.TransactionDate);

                    if (isDeposit)
                    {
                        rows.Add(new BorrowerStatementRow(
                            txn.Id,
                            txn.TransactionDate,
                            formattedDate,
                            0m,
                            LCur(0m),
                            txn.Amount,
                            LCur(txn.Amount),
                            0m,
                            LCur(0m),
                            defaultRateStr,
                            0m,
                            string.Empty,
                            "Deposit"));
                    }
                    else
                    {
                        rows.Add(new BorrowerStatementRow(
                            txn.Id,
                            txn.TransactionDate,
                            formattedDate,
                            txn.Amount,
                            LCur(txn.Amount),
                            0m,
                            LCur(0m),
                            0m,
                            LCur(0m),
                            defaultRateStr,
                            0m,
                            string.Empty,
                            "Withdrawal"));
                    }
                }
            }

            if (interestResult?.Segments != null)
            {
                foreach (var segment in interestResult.Segments)
                {
                    if (segment != null && segment.CalculatedInterest > 0m)
                    {
                        string formattedDate = LDate(segment.SegmentEndDate);
                        string rateStr = $"{LocalizeDigits(segment.ApplicableMonthlyRate.ToString("0.0"))}%";

                        rows.Add(new BorrowerStatementRow(
                            null,
                            segment.SegmentEndDate,
                            formattedDate,
                            0m,
                            LCur(0m),
                            0m,
                            LCur(0m),
                            segment.CalculatedInterest,
                            LCur(segment.CalculatedInterest),
                            rateStr,
                            0m,
                            string.Empty,
                            "Interest"));
                    }
                }
            }

            var orderedRows = rows
                .OrderBy(r => r.Date)
                .ThenBy(r => r.RowType == "Interest" ? 0 : (r.RowType == "Withdrawal" ? 1 : 2))
                .ToList();

            decimal runningBalance = 0m;
            var finalRows = new List<BorrowerStatementRow>();

            foreach (var row in orderedRows)
            {
                if (row.RowType == "Deposit")
                {
                    runningBalance -= row.Deposit;
                }
                else if (row.RowType == "Withdrawal")
                {
                    runningBalance += row.Withdrawal;
                }
                else if (row.RowType == "Interest")
                {
                    runningBalance += row.InterestAmount;
                }

                string formattedBalance = LCur(runningBalance);
                finalRows.Add(row with
                {
                    RunningBalance = runningBalance,
                    FormattedRunningBalance = formattedBalance
                });
            }

            _allStatementRows = new ObservableCollection<BorrowerStatementRow>(finalRows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build statement rows for BorrowerId '{Id}'.", BorrowerId);
            _allStatementRows = new ObservableCollection<BorrowerStatementRow>();
        }
    }

    private void SetTypeFilter(string filterType)
    {
        SelectedTypeFilter = filterType;
    }

    private void ApplyFilters()
    {
        try
        {
            IEnumerable<BorrowerStatementRow> rows = _allStatementRows ?? new ObservableCollection<BorrowerStatementRow>();

            if (!string.Equals(SelectedTypeFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(SelectedTypeFilter, "Given", StringComparison.OrdinalIgnoreCase))
                {
                    rows = rows.Where(r => r != null && (r.RowType == "Withdrawal" || r.Withdrawal > 0m));
                }
                else if (string.Equals(SelectedTypeFilter, "Received", StringComparison.OrdinalIgnoreCase))
                {
                    rows = rows.Where(r => r != null && (r.RowType == "Deposit" || r.Deposit > 0m));
                }
                else if (string.Equals(SelectedTypeFilter, "Interest", StringComparison.OrdinalIgnoreCase))
                {
                    rows = rows.Where(r => r != null && (r.RowType == "Interest" || r.InterestAmount > 0m));
                }
            }

            DisplayStatementRows = new ObservableCollection<BorrowerStatementRow>(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying statement filters for BorrowerId '{Id}'.", BorrowerId);
            DisplayStatementRows = new ObservableCollection<BorrowerStatementRow>();
        }
        finally
        {
            OnPropertyChanged(nameof(HasStatementRows));
            OnPropertyChanged(nameof(HasNoStatementRows));
        }
    }

    private void NotifyAllProperties()
    {
        OnPropertyChanged(nameof(HasBorrowerPhoto));
        OnPropertyChanged(nameof(HasOrnamentPhoto));
        OnPropertyChanged(nameof(BorrowerName));
        OnPropertyChanged(nameof(BorrowerNumber));
        OnPropertyChanged(nameof(FatherName));
        OnPropertyChanged(nameof(Surname));
        OnPropertyChanged(nameof(Village));
        OnPropertyChanged(nameof(BorrowerPhone));
        OnPropertyChanged(nameof(BorrowerStatus));
        OnPropertyChanged(nameof(BorrowerStatusBrushValue));
        OnPropertyChanged(nameof(IsClosed));
        OnPropertyChanged(nameof(AccountClosedDate));
        OnPropertyChanged(nameof(AccountClosedDateText));
        OnPropertyChanged(nameof(AccountClosedSeparatorText));
        OnPropertyChanged(nameof(DisplayLoanType));
        OnPropertyChanged(nameof(DisplayLoanAmount));
        OnPropertyChanged(nameof(DisplayLoanDate));
        OnPropertyChanged(nameof(CurrentPrincipal));
        OnPropertyChanged(nameof(MonthlyInterestRate));
        OnPropertyChanged(nameof(DisplayMonthlyInterestRate));
        OnPropertyChanged(nameof(MonthlyInterest));
        OnPropertyChanged(nameof(DisplayMonthlyInterest));
        OnPropertyChanged(nameof(BorrowerDuration));
        OnPropertyChanged(nameof(DisplayBorrowerDuration));
        OnPropertyChanged(nameof(DisplayCompletedMonths));
        OnPropertyChanged(nameof(HasOrnamentDetails));
        OnPropertyChanged(nameof(IsOrnamentLoanType));
        OnPropertyChanged(nameof(IsOrnamentSectionVisible));
        OnPropertyChanged(nameof(DisplayOrnamentType));
        OnPropertyChanged(nameof(DisplayOrnamentWeight));
        OnPropertyChanged(nameof(SubtitleHeader));
        OnPropertyChanged(nameof(MaskedAadharNumber));
        OnPropertyChanged(nameof(TotalGiven));
        OnPropertyChanged(nameof(TotalReceived));
        OnPropertyChanged(nameof(TotalWithdrawals));
        OnPropertyChanged(nameof(TotalInterest));
        OnPropertyChanged(nameof(OutstandingPrincipal));
        OnPropertyChanged(nameof(OutstandingInterest));
        OnPropertyChanged(nameof(TotalOutstanding));
        OnPropertyChanged(nameof(FormattedTotalGiven));
        OnPropertyChanged(nameof(FormattedTotalReceived));
        OnPropertyChanged(nameof(FormattedTotalWithdrawals));
        OnPropertyChanged(nameof(FormattedTotalInterest));
        OnPropertyChanged(nameof(FormattedOutstandingPrincipal));
        OnPropertyChanged(nameof(FormattedOutstandingInterest));
        OnPropertyChanged(nameof(FormattedTotalOutstanding));
        OnPropertyChanged(nameof(FormattedCreatedAt));
        OnPropertyChanged(nameof(FormattedUpdatedAt));
        OnPropertyChanged(nameof(QrCodeImage));
        OnPropertyChanged(nameof(FormattedBorrowerNumber));
        OnPropertyChanged(nameof(HasStatementRows));
        OnPropertyChanged(nameof(HasNoStatementRows));
    }

    public async Task ReopenAccountAsync()
    {
        if (BorrowerId == Guid.Empty || !IsClosed) return;
        try
        {
            var updated = await _borrowerService.ChangeStatusAsync(BorrowerId, Domain.Enums.BorrowerStatus.Active);
            Borrower = updated;
            await LoadAsync();
            _logger.LogInformation("Borrower account reopened. ID='{Id}'.", BorrowerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reopen account for borrower '{Id}'.", BorrowerId);
            HasError = true;
            ErrorMessage = _localizationService.GetString("ReopenAccountFailed");
        }
    }

    private async Task ChangeStatusAsync(Domain.Enums.BorrowerStatus status)
    {
        try
        {
            var updated = await _borrowerService.ChangeStatusAsync(BorrowerId, status);
            Borrower = updated;
            NotifyAllProperties();
            _logger.LogInformation("Borrower status changed. ID='{Id}', Status='{Status}'.", BorrowerId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change borrower status '{Id}'.", BorrowerId);
            HasError = true;
            ErrorMessage = _localizationService.GetString("StatusUpdateFailed");
        }
    }
}

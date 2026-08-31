using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Common.Exceptions;
using DhirDhar.Application.Localization;
using DhirDhar.Desktop.Helpers;
using DhirDhar.Desktop.ViewModels;
using DhirDhar.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DhirDhar.Desktop.ViewModels.Borrowers;

public sealed record LoanTypeOption(string Value, string Label);

public sealed record OrnamentTypeOption(string Value, string Label);

public sealed class BorrowerEditViewModel : ViewModelBase
{
    private static readonly string[] PredefinedOrnamentTypes = new[]
    {
        "Ring", "Necklace", "Chain", "Bracelet", "Bangle", "Chudi", "Earrings",
        "Pendant", "Nose Ring", "Anklet", "Waist Chain", "Mangalsutra"
    };

    private readonly IBorrowerService _borrowerService;
    private readonly ILocalizationService _localizationService;
    private readonly ITranslationService? _translationService;
    private readonly Services.IInputLanguageService? _inputLanguageService;
    private readonly ILogger<BorrowerEditViewModel> _logger;

    private Guid _borrowerId;
    private string _name = string.Empty;
    private string _fatherName = string.Empty;
    private string _surname = string.Empty;
    private string _village = string.Empty;
    private string _mobileNumber = string.Empty;
    private string _aadharNumber = string.Empty;
    private string _borrowerNumber = string.Empty;

    private string _selectedLoanType = string.Empty;
    private string _selectedOrnamentType = string.Empty;
    private string _customOrnamentType = string.Empty;
    private string _ornamentWeightText = string.Empty;
    private string _loanAmountText = string.Empty;
    private string _interestRateText = "3.00";
    private DateTimeOffset? _loanDate = DateTimeOffset.Now.Date;

    private string? _borrowerPhotoPath;
    private string? _ornamentPhotoPath;

    private ImageSource? _borrowerPhotoPreview;
    private ImageSource? _ornamentPhotoPreview;

    private bool _isNew = true;
    private bool _isSaving;

    private string _fullName = string.Empty;
    private string _fullNameError = string.Empty;
    private string _villageError = string.Empty;
    private string _mobileNumberError = string.Empty;
    private string _aadharNumberError = string.Empty;
    private string _loanTypeError = string.Empty;
    private string _ornamentTypeError = string.Empty;
    private string _customOrnamentTypeError = string.Empty;
    private string _ornamentWeightError = string.Empty;
    private string _loanAmountError = string.Empty;
    private string _interestRateError = string.Empty;
    private string _loanDateError = string.Empty;
    private string _borrowerNumberError = string.Empty;
    private string _photoErrorMessage = string.Empty;

    private bool _isClosed;
    private string _status = string.Empty;
    private bool _isReopenAccountDialogOpen;
    private bool _isReopeningAccount;
    private string _reopenAccountValidationError = string.Empty;

    public BorrowerEditViewModel(
        IBorrowerService borrowerService,
        ILocalizationService localizationService,
        ILogger<BorrowerEditViewModel> logger,
        ITranslationService? translationService = null,
        Services.IInputLanguageService? inputLanguageService = null)
    {
        _borrowerService = borrowerService;
        _localizationService = localizationService;
        _translationService = translationService;
        _logger = logger;
        _inputLanguageService = inputLanguageService;

        _localizationService.LanguageChanged += OnLanguageChanged;

        SaveCommand = new RelayCommand(async () => await SaveAsync(), () => !IsSaving);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());

        TakeBorrowerPhotoCommand = new RelayCommand(async () => await TakeBorrowerPhotoAsync());
        RetakeBorrowerPhotoCommand = new RelayCommand(async () => await TakeBorrowerPhotoAsync());
        RemoveBorrowerPhotoCommand = new RelayCommand(RemoveBorrowerPhoto);

        TakeOrnamentPhotoCommand = new RelayCommand(async () => await TakeOrnamentPhotoAsync());
        RetakeOrnamentPhotoCommand = new RelayCommand(async () => await TakeOrnamentPhotoAsync());
        RemoveOrnamentPhotoCommand = new RelayCommand(RemoveOrnamentPhoto);

        OpenReopenAccountDialogCommand = new RelayCommand(OpenReopenAccountDialog);
        CancelReopenAccountCommand = new RelayCommand(CancelReopenAccountDialog);
        ConfirmReopenAccountCommand = new RelayCommand(async () => await ConfirmReopenAccountAsync(), () => !IsReopeningAccount);
    }

    public event Action? CloseRequested;

    public nint WindowHandle { get; set; }
    public XamlRoot? XamlRoot { get; set; }

    public Guid BorrowerId
    {
        get => _borrowerId;
        private set => SetProperty(ref _borrowerId, value);
    }

    private string ProcessIndicTextInput(string input)
    {
        // Single authoritative engine is IndicInput (PreviewKeyDown) via InputLanguageService.
        // ViewModel must not transliterate; it stores exactly what the UI composition produced.
        return input ?? string.Empty;
    }

    private string ProcessIndicDigitInput(string input)
    {
        // Numbers must remain ASCII for storage; UI may localize for display elsewhere.
        if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;
        return DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(input);
    }

    public bool IsIndicLanguageSelected =>
        !string.IsNullOrWhiteSpace(_localizationService.CurrentLanguage) &&
        !_localizationService.CurrentLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public string ActiveLanguageName =>
        _localizationService.SupportedLanguages.FirstOrDefault(l => l.Code.Equals(_localizationService.CurrentLanguage, StringComparison.OrdinalIgnoreCase))?.NativeName ?? _localizationService.CurrentLanguage;

    public bool IsPhoneticModeSelected => IsIndicLanguageSelected;
    public bool IsNativeImeModeSelected => false;
    public bool IsEnglishModeSelected => !IsIndicLanguageSelected;

    private void NotifyInputModeProperties()
    {
        OnPropertyChanged(nameof(IsPhoneticModeSelected));
        OnPropertyChanged(nameof(IsNativeImeModeSelected));
        OnPropertyChanged(nameof(IsEnglishModeSelected));
        OnPropertyChanged(nameof(IsIndicLanguageSelected));
        OnPropertyChanged(nameof(ActiveLanguageName));
    }

    public string FullName
    {
        get => _fullName;
        set
        {
            var processed = ProcessIndicTextInput(value);
            if (SetProperty(ref _fullName, processed) && !string.IsNullOrEmpty(FullNameError))
            {
                FullNameError = string.Empty;
            }
        }
    }

    public string Name
    {
        get => _fullName;
        set => FullName = value;
    }

    public string FatherName
    {
        get => string.Empty;
        set { }
    }

    public string Surname
    {
        get => string.Empty;
        set { }
    }

    public string Village
    {
        get => _village;
        set
        {
            var processed = ProcessIndicTextInput(value);
            if (SetProperty(ref _village, processed) && !string.IsNullOrEmpty(VillageError))
            {
                VillageError = string.Empty;
            }
        }
    }

    public string MobileNumber
    {
        get => LocalizeDigits(_mobileNumber);
        set
        {
            var normalized = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(value ?? string.Empty);
            if (SetProperty(ref _mobileNumber, normalized) && !string.IsNullOrEmpty(MobileNumberError))
            {
                MobileNumberError = string.Empty;
            }
            OnPropertyChanged(nameof(MobileNumber));
        }
    }

    public string AadharNumber
    {
        get => LocalizeDigits(_aadharNumber);
        set
        {
            var normalized = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(value ?? string.Empty);
            if (SetProperty(ref _aadharNumber, normalized) && !string.IsNullOrEmpty(AadharNumberError))
            {
                AadharNumberError = string.Empty;
            }
            OnPropertyChanged(nameof(AadharNumber));
        }
    }

    private string _borrowerPrefix = BorrowerNumberHelper.DefaultPrefix;
    private string _borrowerSequenceNumber = string.Empty;

    public string BorrowerPrefix
    {
        get => _borrowerPrefix;
        set => SetProperty(ref _borrowerPrefix, value);
    }

    public string BorrowerSequenceNumber
    {
        get => LocalizeDigits(_borrowerSequenceNumber);
        set
        {
            var normalized = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(value ?? string.Empty);
            if (SetProperty(ref _borrowerSequenceNumber, normalized))
            {
                if (!string.IsNullOrEmpty(BorrowerNumberError))
                {
                    BorrowerNumberError = string.Empty;
                }
                _borrowerNumber = string.IsNullOrWhiteSpace(normalized)
                    ? string.Empty
                    : BorrowerNumberHelper.CombinePrefixAndSequence(_borrowerPrefix, normalized);
                OnPropertyChanged(nameof(BorrowerSequenceNumber));
                OnPropertyChanged(nameof(BorrowerNumber));
            }
        }
    }

    public string BorrowerNumber
    {
        get => LocalizeDigits(_borrowerNumber);
        set
        {
            var normalized = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(value ?? string.Empty);
            if (SetProperty(ref _borrowerNumber, normalized))
            {
                if (!string.IsNullOrEmpty(BorrowerNumberError))
                {
                    BorrowerNumberError = string.Empty;
                }

                if (BorrowerNumberHelper.TryParseSequence(normalized, _borrowerPrefix, out var seq))
                {
                    _borrowerSequenceNumber = BorrowerNumberHelper.FormatSequence(seq);
                }
                else
                {
                    _borrowerSequenceNumber = normalized;
                }

                OnPropertyChanged(nameof(BorrowerNumber));
                OnPropertyChanged(nameof(BorrowerSequenceNumber));
            }
        }
    }

    public string SelectedLoanType
    {
        get => _selectedLoanType;
        set
        {
            if (SetProperty(ref _selectedLoanType, value))
            {
                if (!string.IsNullOrEmpty(LoanTypeError))
                {
                    LoanTypeError = string.Empty;
                }
                OnPropertyChanged(nameof(IsOrnamentSectionVisible));
                OnPropertyChanged(nameof(IsOrnamentPhotoVisible));
                OnPropertyChanged(nameof(IsCustomOrnamentTypeVisible));
            }
        }
    }

    public string SelectedOrnamentType
    {
        get => _selectedOrnamentType;
        set
        {
            if (SetProperty(ref _selectedOrnamentType, value))
            {
                if (!string.IsNullOrEmpty(OrnamentTypeError))
                {
                    OrnamentTypeError = string.Empty;
                }
                if (!string.Equals(value, "Other", StringComparison.OrdinalIgnoreCase))
                {
                    CustomOrnamentType = string.Empty;
                    CustomOrnamentTypeError = string.Empty;
                }
                OnPropertyChanged(nameof(IsCustomOrnamentTypeVisible));
            }
        }
    }

    public string CustomOrnamentType
    {
        get => _customOrnamentType;
        set
        {
            var processed = ProcessIndicTextInput(value);
            if (SetProperty(ref _customOrnamentType, processed) && !string.IsNullOrEmpty(CustomOrnamentTypeError))
            {
                CustomOrnamentTypeError = string.Empty;
            }
        }
    }

    public string OrnamentWeightText
    {
        get => LocalizeDigits(_ornamentWeightText);
        set
        {
            var normalized = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(value ?? string.Empty);
            if (SetProperty(ref _ornamentWeightText, normalized) && !string.IsNullOrEmpty(OrnamentWeightError))
            {
                OrnamentWeightError = string.Empty;
            }
            OnPropertyChanged(nameof(OrnamentWeightText));
        }
    }

    public string LoanAmountText
    {
        get => LocalizeDigits(_loanAmountText);
        set
        {
            var normalized = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(value ?? string.Empty);
            if (SetProperty(ref _loanAmountText, normalized) && !string.IsNullOrEmpty(LoanAmountError))
            {
                LoanAmountError = string.Empty;
            }
            OnPropertyChanged(nameof(LoanAmountText));
        }
    }

    public string InterestRateText
    {
        get => LocalizeDigits(_interestRateText);
        set
        {
            var normalized = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(value ?? string.Empty);
            if (SetProperty(ref _interestRateText, normalized) && !string.IsNullOrEmpty(InterestRateError))
            {
                InterestRateError = string.Empty;
            }
            OnPropertyChanged(nameof(InterestRateText));
        }
    }

    public DateTimeOffset? LoanDate
    {
        get => _loanDate;
        set
        {
            if (SetProperty(ref _loanDate, value) && !string.IsNullOrEmpty(LoanDateError))
            {
                LoanDateError = string.Empty;
            }
        }
    }

    public string? BorrowerPhotoPath
    {
        get => _borrowerPhotoPath;
        set
        {
            if (SetProperty(ref _borrowerPhotoPath, value))
            {
                OnPropertyChanged(nameof(HasBorrowerPhoto));
                UpdateBorrowerPhotoPreview();
            }
        }
    }

    public string? OrnamentPhotoPath
    {
        get => _ornamentPhotoPath;
        set
        {
            if (SetProperty(ref _ornamentPhotoPath, value))
            {
                OnPropertyChanged(nameof(HasOrnamentPhoto));
                UpdateOrnamentPhotoPreview();
            }
        }
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

    public bool HasBorrowerPhoto => !string.IsNullOrWhiteSpace(BorrowerPhotoPath) && File.Exists(BorrowerPhotoPath);
    public bool HasOrnamentPhoto => !string.IsNullOrWhiteSpace(OrnamentPhotoPath) && File.Exists(OrnamentPhotoPath);

    public bool IsOrnamentSectionVisible =>
        string.Equals(SelectedLoanType, "Gold", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(SelectedLoanType, "Silver", StringComparison.OrdinalIgnoreCase);

    public bool IsOrnamentPhotoVisible => IsOrnamentSectionVisible;

    public bool IsCustomOrnamentTypeVisible =>
        IsOrnamentSectionVisible &&
        string.Equals(SelectedOrnamentType, "Other", StringComparison.OrdinalIgnoreCase);

    public bool IsNew
    {
        get => _isNew;
        private set
        {
            if (SetProperty(ref _isNew, value))
            {
                OnPropertyChanged(nameof(PageTitle));
                OnPropertyChanged(nameof(IsCreateMode));
                OnPropertyChanged(nameof(IsEditMode));
            }
        }
    }

    public bool IsCreateMode => IsNew;
    public bool IsEditMode => !IsNew;

    public string PageTitle => IsNew ? _localizationService.GetString("AddBorrower") : _localizationService.GetString("EditBorrower");

    public bool IsSaving
    {
        get => _isSaving;
        private set => SetProperty(ref _isSaving, value);
    }

    public string FullNameError
    {
        get => _fullNameError;
        private set => SetProperty(ref _fullNameError, value);
    }

    public string NameError
    {
        get => _fullNameError;
        private set => FullNameError = value;
    }

    public string FatherNameError
    {
        get => string.Empty;
        private set { }
    }

    public string SurnameError
    {
        get => string.Empty;
        private set { }
    }

    public string VillageError
    {
        get => _villageError;
        private set => SetProperty(ref _villageError, value);
    }

    public string MobileNumberError
    {
        get => _mobileNumberError;
        private set => SetProperty(ref _mobileNumberError, value);
    }

    public string AadharNumberError
    {
        get => _aadharNumberError;
        private set => SetProperty(ref _aadharNumberError, value);
    }

    public string LoanTypeError
    {
        get => _loanTypeError;
        private set => SetProperty(ref _loanTypeError, value);
    }

    public string OrnamentTypeError
    {
        get => _ornamentTypeError;
        private set => SetProperty(ref _ornamentTypeError, value);
    }

    public string CustomOrnamentTypeError
    {
        get => _customOrnamentTypeError;
        private set => SetProperty(ref _customOrnamentTypeError, value);
    }

    public string OrnamentWeightError
    {
        get => _ornamentWeightError;
        private set => SetProperty(ref _ornamentWeightError, value);
    }

    public string LoanAmountError
    {
        get => _loanAmountError;
        private set => SetProperty(ref _loanAmountError, value);
    }

    public string InterestRateError
    {
        get => _interestRateError;
        private set => SetProperty(ref _interestRateError, value);
    }

    public string LoanDateError
    {
        get => _loanDateError;
        private set => SetProperty(ref _loanDateError, value);
    }

    public string BorrowerNumberError
    {
        get => _borrowerNumberError;
        private set => SetProperty(ref _borrowerNumberError, value);
    }

    public string PhotoErrorMessage
    {
        get => _photoErrorMessage;
        private set => SetProperty(ref _photoErrorMessage, value);
    }

    public string InputModeLabel => _localizationService.GetString("InputMode");
    public string PhoneticInputModeLabel => _localizationService.GetString("InputModePhonetic");
    public string NativeImeInputModeLabel => _localizationService.GetString("InputModeNative");
    public string EnglishLatinInputModeLabel => _localizationService.GetString("InputModeEnglish");

    public string FullNameLabel => _localizationService.GetString("FullName");
    public string FullNamePlaceholder => _localizationService.GetString("FullNamePlaceholder");
    public string NameLabel => _localizationService.GetString("FullName");
    public string FatherNameLabel => string.Empty;
    public string SurnameLabel => string.Empty;
    public string VillageLabel => _localizationService.GetString("Village");
    public string VillagePlaceholder => _localizationService.GetString("VillagePlaceholder");
    public string MobileNumberLabel => _localizationService.GetString("MobileNumber");
    public string MobileNumberPlaceholder => _localizationService.GetString("MobileNumberPlaceholder");
    public string AadharNumberLabel => _localizationService.GetString("AadharNumber");
    public string AadharNumberPlaceholder => _localizationService.GetString("AadharNumberPlaceholder");
    public string BorrowerNumberLabel => _localizationService.GetString("BorrowerNumber");
    public string BorrowerNumberPlaceholder => _localizationService.GetString("BorrowerNumberPlaceholder");
    public string LoanAmountLabel => _localizationService.GetString("LoanAmount");
    public string InterestRateLabel => _localizationService.GetString("InterestRate");
    public string LoanDateLabel => _localizationService.GetString("LoanDate");
    public string LoanAmountPlaceholder => _localizationService.GetString("LoanAmountPlaceholder");
    public string InterestRatePlaceholder => _localizationService.GetString("InterestRatePlaceholder");
    public string OptionalLabel => _localizationService.GetString("Optional");
    public string CancelText => _localizationService.GetString("Cancel");
    public string SaveBorrowerText => _localizationService.GetString("SaveBorrower");
    public string SubtitleText => _localizationService.GetString("BorrowerFormSubtitle");
    public string LoanTypeLabel => _localizationService.GetString("LoanType");
    public string SelectLoanTypeLabel => _localizationService.GetString("SelectLoanType");
    public string OrnamentTypeLabel => _localizationService.GetString("OrnamentType");
    public string SelectOrnamentCategoryLabel => _localizationService.GetString("SelectOrnamentCategory");
    public string OrnamentWeightLabel => _localizationService.GetString("OrnamentWeight");
    public string GramsLabel => _localizationService.GetString("Grams");
    public string EnterOrnamentTypeLabel => _localizationService.GetString("EnterOrnamentType");
    public string EnterOrnamentTypePlaceholderLabel => _localizationService.GetString("EnterOrnamentTypePlaceholder");
    public string PercentPerMonthLabel => _localizationService.GetString("PercentPerMonth");
    public string BorrowerPhotoLabel => _localizationService.GetString("BorrowerPhoto");
    public string NoBorrowerPhotoCapturedLabel => _localizationService.GetString("NoBorrowerPhotoCaptured");
    public string TakePhotoLabel => _localizationService.GetString("TakePhoto");
    public string RetakePhotoLabel => _localizationService.GetString("RetakePhoto");
    public string RemovePhotoLabel => _localizationService.GetString("RemovePhoto");
    public string OrnamentPhotoLabel => _localizationService.GetString("OrnamentPhoto");
    public string GoldSilverLabel => _localizationService.GetString("GoldSilver");
    public string NoOrnamentPhotoCapturedLabel => _localizationService.GetString("NoOrnamentPhotoCaptured");

    public bool IsClosed
    {
        get => _isClosed;
        private set => SetProperty(ref _isClosed, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsReopenAccountDialogOpen
    {
        get => _isReopenAccountDialogOpen;
        set => SetProperty(ref _isReopenAccountDialogOpen, value);
    }

    public bool IsReopeningAccount
    {
        get => _isReopeningAccount;
        private set
        {
            if (SetProperty(ref _isReopeningAccount, value))
            {
                ConfirmReopenAccountCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string ReopenAccountValidationError
    {
        get => _reopenAccountValidationError;
        private set
        {
            if (SetProperty(ref _reopenAccountValidationError, value))
            {
                OnPropertyChanged(nameof(HasReopenAccountValidationError));
            }
        }
    }

    public bool HasReopenAccountValidationError => !string.IsNullOrWhiteSpace(ReopenAccountValidationError);

    public string ReopenAccountLabel => _localizationService.GetString("ReopenAccount");
    public string ConfirmReopenAccountTitleLabel => _localizationService.GetString("ConfirmReopenAccountTitle");
    public string ConfirmReopenAccountMessageLabel => _localizationService.GetString("ConfirmReopenAccountMessage");
    public string ClosedStatusLabel => _localizationService.GetString("Closed");
    public string ClosedAccountBannerTitle => _localizationService.GetString("Closed");
    public string ClosedAccountBannerDescription => _localizationService.GetString("CloseAccountInfo");

    public IReadOnlyList<LoanTypeOption> LoanTypeOptions => new[]
    {
        new LoanTypeOption("Cash", _localizationService.GetString("Cash")),
        new LoanTypeOption("Gold", _localizationService.GetString("Gold")),
        new LoanTypeOption("Silver", _localizationService.GetString("Silver"))
    };

    public IReadOnlyList<OrnamentTypeOption> OrnamentTypeOptions => PredefinedOrnamentTypes
        .Append("Other")
        .Select(o => new OrnamentTypeOption(o, _localizationService.GetString(o)))
        .ToList();

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public RelayCommand TakeBorrowerPhotoCommand { get; }
    public RelayCommand RetakeBorrowerPhotoCommand { get; }
    public RelayCommand RemoveBorrowerPhotoCommand { get; }

    public RelayCommand TakeOrnamentPhotoCommand { get; }
    public RelayCommand RetakeOrnamentPhotoCommand { get; }
    public RelayCommand RemoveOrnamentPhotoCommand { get; }

    public RelayCommand OpenReopenAccountDialogCommand { get; }
    public RelayCommand CancelReopenAccountCommand { get; }
    public RelayCommand ConfirmReopenAccountCommand { get; }

    public void OpenReopenAccountDialog()
    {
        ReopenAccountValidationError = string.Empty;
        IsReopenAccountDialogOpen = true;
    }

    public void CancelReopenAccountDialog()
    {
        IsReopenAccountDialogOpen = false;
        ReopenAccountValidationError = string.Empty;
    }

    public async Task ConfirmReopenAccountAsync()
    {
        if (BorrowerId == Guid.Empty || !IsClosed) return;

        IsReopeningAccount = true;
        ReopenAccountValidationError = string.Empty;

        try
        {
            await _borrowerService.ChangeStatusAsync(BorrowerId, Domain.Enums.BorrowerStatus.Active).ConfigureAwait(false);
            IsClosed = false;
            Status = Domain.Enums.BorrowerStatus.Active.ToString();
            IsReopenAccountDialogOpen = false;
            _logger.LogInformation("Borrower account reopened from Edit Borrower interface. ID='{BorrowerId}'.", BorrowerId);

            if (App.MainDispatcherQueue is { } dispatcherQueue)
            {
                dispatcherQueue.TryEnqueue(() => CloseRequested?.Invoke());
            }
            else
            {
                CloseRequested?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reopen account for borrower '{BorrowerId}' in Edit Borrower interface.", BorrowerId);
            ReopenAccountValidationError = _localizationService.GetString("ReopenAccountFailed");
        }
        finally
        {
            IsReopeningAccount = false;
        }
    }

    public async Task LoadBorrowerAsync(Guid id)
    {
        IsNew = false;
        try
        {
            var borrower = await _borrowerService.GetByIdAsync(id).ConfigureAwait(false);
            if (borrower is null)
            {
                ClearFields();
                return;
            }

            var currentLang = _localizationService.CurrentLanguage;
            var localizedBorrower = borrower.Localize(_translationService, currentLang);

            BorrowerId = localizedBorrower.Id;
            var currentPrefix = await _borrowerService.GetBorrowerPrefixAsync().ConfigureAwait(false);
            BorrowerPrefix = string.IsNullOrWhiteSpace(currentPrefix) ? BorrowerNumberHelper.DefaultPrefix : currentPrefix;
            BorrowerNumber = localizedBorrower.BorrowerNumber ?? string.Empty;
            _fullName = localizedBorrower.FullName;
            OnPropertyChanged(nameof(FullName));
            OnPropertyChanged(nameof(Name));
            _village = localizedBorrower.Village ?? string.Empty;
            OnPropertyChanged(nameof(Village));
            MobileNumber = localizedBorrower.Contact ?? string.Empty;
            AadharNumber = localizedBorrower.AadharNumber ?? string.Empty;
            SelectedLoanType = borrower.LoanType ?? string.Empty;

            var ornament = borrower.OrnamentType ?? string.Empty;
            if (string.IsNullOrWhiteSpace(ornament))
            {
                SelectedOrnamentType = string.Empty;
                _customOrnamentType = string.Empty;
                OnPropertyChanged(nameof(CustomOrnamentType));
            }
            else
            {
                var matchingPredefined = PredefinedOrnamentTypes.FirstOrDefault(p =>
                    string.Equals(p, ornament, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_localizationService.GetString(p), ornament, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_translationService?.Translate(p, "gu-IN"), ornament, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_translationService?.Translate(p, "en-US"), ornament, StringComparison.OrdinalIgnoreCase));

                if (matchingPredefined != null)
                {
                    SelectedOrnamentType = matchingPredefined;
                    _customOrnamentType = string.Empty;
                    OnPropertyChanged(nameof(CustomOrnamentType));
                }
                else
                {
                    SelectedOrnamentType = "Other";
                    _customOrnamentType = _translationService != null
                        ? _translationService.Translate(ornament, currentLang)
                        : ScriptTranslator.Translate(ornament, currentLang);
                    OnPropertyChanged(nameof(CustomOrnamentType));
                }
            }

            OrnamentWeightText = borrower.OrnamentWeight.HasValue ? LocalizeDigits(borrower.OrnamentWeight.Value.ToString("0.00", CultureInfo.InvariantCulture)) : string.Empty;
            LoanAmountText = borrower.LoanAmount.HasValue ? LocalizeDigits(borrower.LoanAmount.Value.ToString("0.00", CultureInfo.InvariantCulture)) : string.Empty;
            InterestRateText = borrower.InterestRate.HasValue ? LocalizeDigits(borrower.InterestRate.Value.ToString("0.00", CultureInfo.InvariantCulture)) : LocalizeDigits("3.00");
            LoanDate = borrower.LoanDate.HasValue ? new DateTimeOffset(borrower.LoanDate.Value) : null;
            BorrowerPhotoPath = borrower.BorrowerPhotoPath;
            OrnamentPhotoPath = borrower.OrnamentPhotoPath;
            Status = borrower.Status;
            IsClosed = string.Equals(borrower.Status, "Closed", StringComparison.OrdinalIgnoreCase);

            ClearErrors();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load borrower '{Id}' for editing.", id);
            ClearFields();
        }
    }

    public void SetAsNew()
    {
        IsNew = true;
        BorrowerId = Guid.Empty;
        ClearFields();
        InterestRateText = "3.00";
        LoanDate = DateTimeOffset.Now.Date;
        _ = RefreshNextBorrowerNumberAsync();
    }

    public async Task RefreshNextBorrowerNumberAsync()
    {
        if (IsNew)
        {
            try
            {
                var prefix = await _borrowerService.GetBorrowerPrefixAsync().ConfigureAwait(false);
                var nextNumber = await _borrowerService.GetNextBorrowerNumberAsync().ConfigureAwait(false);

                var safePrefix = string.IsNullOrWhiteSpace(prefix) ? BorrowerNumberHelper.DefaultPrefix : prefix.Trim();
                var safeNextNumber = string.IsNullOrWhiteSpace(nextNumber) ? BorrowerNumberHelper.FormatBorrowerNumber(safePrefix, 1) : nextNumber.Trim();

                if (App.MainDispatcherQueue is { } dispatcherQueue)
                {
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        BorrowerPrefix = safePrefix;
                        BorrowerNumber = safeNextNumber;
                    });
                }
                else
                {
                    BorrowerPrefix = safePrefix;
                    BorrowerNumber = safeNextNumber;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch next borrower number. Applying default fallback.");
                var fallbackPrefix = string.IsNullOrWhiteSpace(BorrowerPrefix) ? BorrowerNumberHelper.DefaultPrefix : BorrowerPrefix;
                var fallbackNumber = BorrowerNumberHelper.FormatBorrowerNumber(fallbackPrefix, 1);

                if (App.MainDispatcherQueue is { } dispatcherQueue)
                {
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        BorrowerPrefix = fallbackPrefix;
                        BorrowerNumber = fallbackNumber;
                    });
                }
                else
                {
                    BorrowerPrefix = fallbackPrefix;
                    BorrowerNumber = fallbackNumber;
                }
            }
        }
    }

    public void SetForEdit(Guid id)
    {
        IsNew = false;
        BorrowerId = id;
        ClearFields();
    }

    public async Task LoadAsync()
    {
        if (!IsNew && BorrowerId != Guid.Empty)
        {
            await LoadBorrowerAsync(BorrowerId).ConfigureAwait(false);
        }
        else if (IsNew)
        {
            await RefreshNextBorrowerNumberAsync().ConfigureAwait(false);
        }
    }

    private void ClearFields()
    {
        _fullName = string.Empty;
        OnPropertyChanged(nameof(FullName));
        OnPropertyChanged(nameof(Name));
        _village = string.Empty;
        OnPropertyChanged(nameof(Village));
        MobileNumber = string.Empty;
        AadharNumber = string.Empty;
        _borrowerNumber = string.Empty;
        _borrowerSequenceNumber = string.Empty;
        OnPropertyChanged(nameof(BorrowerNumber));
        OnPropertyChanged(nameof(BorrowerSequenceNumber));
        SelectedLoanType = string.Empty;
        SelectedOrnamentType = string.Empty;
        _customOrnamentType = string.Empty;
        OnPropertyChanged(nameof(CustomOrnamentType));
        OrnamentWeightText = string.Empty;
        LoanAmountText = string.Empty;
        InterestRateText = "3.00";
        LoanDate = DateTimeOffset.Now.Date;
        BorrowerPhotoPath = null;
        OrnamentPhotoPath = null;
        BorrowerPhotoPreview = null;
        OrnamentPhotoPreview = null;
        PhotoErrorMessage = string.Empty;
        IsClosed = false;
        Status = string.Empty;
        IsReopenAccountDialogOpen = false;
        ReopenAccountValidationError = string.Empty;
        ClearErrors();
    }

    private void ClearErrors()
    {
        FullNameError = string.Empty;
        VillageError = string.Empty;
        MobileNumberError = string.Empty;
        AadharNumberError = string.Empty;
        LoanTypeError = string.Empty;
        OrnamentTypeError = string.Empty;
        CustomOrnamentTypeError = string.Empty;
        OrnamentWeightError = string.Empty;
        LoanAmountError = string.Empty;
        InterestRateError = string.Empty;
        LoanDateError = string.Empty;
        BorrowerNumberError = string.Empty;
        PhotoErrorMessage = string.Empty;
    }

    private async Task TakeBorrowerPhotoAsync()
    {
        PhotoErrorMessage = string.Empty;
        var path = await CameraPhotoHelper.CaptureOrPickPhotoAsync("borrower", WindowHandle, XamlRoot);
        if (!string.IsNullOrWhiteSpace(path))
        {
            BorrowerPhotoPath = path;
        }
        else if (!HasBorrowerPhoto)
        {
            PhotoErrorMessage = _localizationService.GetString("CameraOrFileCancelled");
        }
    }

    private void RemoveBorrowerPhoto()
    {
        BorrowerPhotoPath = null;
        BorrowerPhotoPreview = null;
    }

    private async Task TakeOrnamentPhotoAsync()
    {
        PhotoErrorMessage = string.Empty;
        var path = await CameraPhotoHelper.CaptureOrPickPhotoAsync("ornament", WindowHandle, XamlRoot);
        if (!string.IsNullOrWhiteSpace(path))
        {
            OrnamentPhotoPath = path;
        }
        else if (!HasOrnamentPhoto)
        {
            PhotoErrorMessage = _localizationService.GetString("CameraOrFileCancelled");
        }
    }

    private void RemoveOrnamentPhoto()
    {
        OrnamentPhotoPath = null;
        OrnamentPhotoPreview = null;
    }

    private void UpdateBorrowerPhotoPreview()
    {
        var path = BorrowerPhotoPath;
        bool exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        var dispatcher = App.MainDispatcherQueue ?? App.MainWindow?.DispatcherQueue;

        if (dispatcher is null)
        {
            try
            {
                if (exists && path != null)
                {
                    var uri = Uri.TryCreate(path, UriKind.Absolute, out var u) ? u : new Uri(path);
                    BorrowerPhotoPreview = new BitmapImage(uri);
                }
                else BorrowerPhotoPreview = null;
            }
            catch { BorrowerPhotoPreview = null; }
            return;
        }

        dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (exists && path != null)
                {
                    var uri = Uri.TryCreate(path, UriKind.Absolute, out var u) ? u : new Uri(path);
                    BorrowerPhotoPreview = new BitmapImage(uri);
                }
                else
                {
                    BorrowerPhotoPreview = null;
                }
            }
            catch
            {
                BorrowerPhotoPreview = null;
            }
        });
    }

    private void UpdateOrnamentPhotoPreview()
    {
        var path = OrnamentPhotoPath;
        bool exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        var dispatcher = App.MainDispatcherQueue ?? App.MainWindow?.DispatcherQueue;

        if (dispatcher is null)
        {
            try
            {
                if (exists && path != null)
                {
                    var uri = Uri.TryCreate(path, UriKind.Absolute, out var u) ? u : new Uri(path);
                    OrnamentPhotoPreview = new BitmapImage(uri);
                }
                else OrnamentPhotoPreview = null;
            }
            catch { OrnamentPhotoPreview = null; }
            return;
        }

        dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (exists && path != null)
                {
                    var uri = Uri.TryCreate(path, UriKind.Absolute, out var u) ? u : new Uri(path);
                    OrnamentPhotoPreview = new BitmapImage(uri);
                }
                else
                {
                    OrnamentPhotoPreview = null;
                }
            }
            catch
            {
                OrnamentPhotoPreview = null;
            }
        });
    }

    public async Task SaveAsync()
    {
        if (IsSaving) return;
        if (!ValidateAll()) return;

        IsSaving = true;
        try
        {
            var asciiLoanAmount = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(LoanAmountText ?? string.Empty);
            var asciiInterestRate = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(InterestRateText ?? string.Empty);
            var asciiWeight = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(OrnamentWeightText ?? string.Empty);

            var parsedLoanAmount = decimal.TryParse(asciiLoanAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) ? amt : (decimal?)null;
            var parsedInterestRate = decimal.TryParse(asciiInterestRate, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate) ? rate : (decimal?)null;
            var parsedWeight = decimal.TryParse(asciiWeight, NumberStyles.Any, CultureInfo.InvariantCulture, out var wt) ? wt : (decimal?)null;
            var loanDate = LoanDate.HasValue ? LoanDate.Value.Date : (DateTime?)null;

            var ornamentType = IsOrnamentSectionVisible
                ? (string.Equals(SelectedOrnamentType, "Other", StringComparison.OrdinalIgnoreCase)
                    ? CustomOrnamentType?.Trim()
                    : SelectedOrnamentType?.Trim())
                : null;

            var finalFullName = ProcessIndicTextInput(FullName?.Trim() ?? string.Empty);
            var finalVillage = ProcessIndicTextInput(Village?.Trim() ?? string.Empty);

            var effectiveBorrowerNumber = !string.IsNullOrWhiteSpace(BorrowerSequenceNumber)
                ? BorrowerNumberHelper.CombinePrefixAndSequence(BorrowerPrefix, BorrowerSequenceNumber)
                : BorrowerNumber;

            if (IsNew)
            {
                var borrowerNumber = string.IsNullOrWhiteSpace(effectiveBorrowerNumber)
                    ? await _borrowerService.GetNextBorrowerNumberAsync().ConfigureAwait(false)
                    : effectiveBorrowerNumber.Trim();
                var entryDate = loanDate.HasValue && loanDate.Value.Date < DateTime.Today ? loanDate.Value.Date : DateTime.Today;
                var contact = string.IsNullOrWhiteSpace(MobileNumber) ? null : DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(MobileNumber.Trim());
                var aadhar = string.IsNullOrWhiteSpace(AadharNumber) ? null : DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(AadharNumber.Trim());

                await _borrowerService.CreateAsync(new CreateBorrowerRequest(
                    borrowerNumber,
                    finalFullName,
                    null,
                    null,
                    finalVillage,
                    contact,
                    null,
                    aadhar,
                    entryDate,
                    parsedLoanAmount,
                    loanDate,
                    null,
                    BorrowerPhotoPath,
                    IsOrnamentSectionVisible ? OrnamentPhotoPath : null,
                    SelectedLoanType,
                    ornamentType,
                    parsedWeight,
                    parsedInterestRate)).ConfigureAwait(false);
                _logger.LogInformation("New borrower created with LoanType '{LoanType}'.", SelectedLoanType);
            }
            else
            {
                var borrowerNumber = string.IsNullOrWhiteSpace(effectiveBorrowerNumber)
                    ? null
                    : effectiveBorrowerNumber.Trim();
                var contact = string.IsNullOrWhiteSpace(MobileNumber) ? null : DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(MobileNumber.Trim());
                var aadhar = string.IsNullOrWhiteSpace(AadharNumber) ? null : DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(AadharNumber.Trim());

                await _borrowerService.UpdateAsync(new UpdateBorrowerRequest(
                    BorrowerId,
                    finalFullName,
                    null,
                    null,
                    finalVillage,
                    contact,
                    null,
                    aadhar,
                    null,
                    BorrowerPhotoPath,
                    IsOrnamentSectionVisible ? OrnamentPhotoPath : null,
                    SelectedLoanType,
                    ornamentType,
                    parsedWeight,
                    parsedLoanAmount,
                    loanDate,
                    parsedInterestRate,
                    borrowerNumber)).ConfigureAwait(false);
                _logger.LogInformation("Borrower updated. ID='{Id}', LoanType='{LoanType}'.", BorrowerId, SelectedLoanType);
            }

            if (_translationService != null)
            {
                var normLang = ScriptTranslator.NormalizeLanguageCode(_localizationService.CurrentLanguage);
                if (!string.IsNullOrWhiteSpace(finalFullName))
                {
                    _ = _translationService.SetTranslationAsync(finalFullName, normLang, finalFullName);
                    var enName = ScriptTranslator.ToEnglish(finalFullName);
                    var guName = ScriptTranslator.ToGujarati(finalFullName);
                    var hiName = ScriptTranslator.ToHindi(finalFullName);
                    _ = _translationService.SetTranslationAsync(finalFullName, "en", enName);
                    _ = _translationService.SetTranslationAsync(finalFullName, "gu", guName);
                    _ = _translationService.SetTranslationAsync(finalFullName, "hi", hiName);
                    _ = _translationService.SetTranslationAsync(enName, "gu", guName);
                    _ = _translationService.SetTranslationAsync(enName, "hi", hiName);
                }

                if (!string.IsNullOrWhiteSpace(finalVillage))
                {
                    _ = _translationService.SetTranslationAsync(finalVillage, normLang, finalVillage);
                    var enVil = ScriptTranslator.ToEnglish(finalVillage);
                    var guVil = ScriptTranslator.ToGujarati(finalVillage);
                    var hiVil = ScriptTranslator.ToHindi(finalVillage);
                    _ = _translationService.SetTranslationAsync(finalVillage, "en", enVil);
                    _ = _translationService.SetTranslationAsync(finalVillage, "gu", guVil);
                    _ = _translationService.SetTranslationAsync(finalVillage, "hi", hiVil);
                    _ = _translationService.SetTranslationAsync(enVil, "gu", guVil);
                    _ = _translationService.SetTranslationAsync(enVil, "hi", hiVil);
                }

                var texts = new[] { finalFullName, finalVillage }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                _ = _translationService.PreloadTranslationsAsync(texts, "en");
                _ = _translationService.PreloadTranslationsAsync(texts, "gu");
                _ = _translationService.PreloadTranslationsAsync(texts, "hi");
            }

            CloseRequested?.Invoke();
        }
        catch (ValidationException ex)
        {
            if (ex.Message.Contains("Borrower number", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                BorrowerNumberError = _localizationService.GetString("BorrowerNumberAlreadyExists");
            }
            else
            {
                MobileNumberError = ex.Message;
            }
            _logger.LogWarning("Validation error saving borrower: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save borrower.");
            MobileNumberError = _localizationService.GetString("SaveBorrowerError");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool ValidateAll()
    {
        ClearErrors();
        bool isValid = true;

        if (string.IsNullOrWhiteSpace(FullName))
        {
            FullNameError = _localizationService.GetString("FullNameRequired");
            isValid = false;
        }
        else if (FullName.Trim().Length > 100)
        {
            FullNameError = _localizationService.GetString("FullNameTooLong");
            isValid = false;
        }

        var seqToValidate = string.IsNullOrWhiteSpace(BorrowerSequenceNumber) ? BorrowerNumber : BorrowerSequenceNumber;
        if (string.IsNullOrWhiteSpace(seqToValidate))
        {
            BorrowerNumberError = _localizationService.GetString("BorrowerNumberRequired");
            isValid = false;
        }
        else if (!Domain.Common.BorrowerNumberHelper.ValidateSequenceInput(seqToValidate, out _, out var bnErrorKey))
        {
            BorrowerNumberError = _localizationService.GetString(bnErrorKey ?? "InvalidBorrowerNumber");
            isValid = false;
        }

        if (IsNew)
        {
            if (string.IsNullOrWhiteSpace(Village))
            {
                VillageError = _localizationService.GetString("VillageRequired");
                isValid = false;
            }

            if (string.IsNullOrWhiteSpace(SelectedLoanType))
            {
                LoanTypeError = _localizationService.GetString("LoanTypeRequired");
                isValid = false;
            }

            if (IsOrnamentSectionVisible)
            {
                if (string.IsNullOrWhiteSpace(SelectedOrnamentType))
                {
                    OrnamentTypeError = _localizationService.GetString("OrnamentTypeRequired");
                    isValid = false;
                }
                else if (IsCustomOrnamentTypeVisible)
                {
                    if (string.IsNullOrWhiteSpace(CustomOrnamentType))
                    {
                        CustomOrnamentTypeError = _localizationService.GetString("CustomOrnamentTypeRequired");
                        isValid = false;
                    }
                }

                if (string.IsNullOrWhiteSpace(OrnamentWeightText))
                {
                    OrnamentWeightError = _localizationService.GetString("OrnamentWeightRequired");
                    isValid = false;
                }
                else
                {
                    var asciiWeight = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(OrnamentWeightText.Trim());
                    if (!decimal.TryParse(asciiWeight, NumberStyles.Any, CultureInfo.InvariantCulture, out var weightVal) || weightVal <= 0m)
                    {
                        OrnamentWeightError = _localizationService.GetString("InvalidOrnamentWeight");
                        isValid = false;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(LoanAmountText))
            {
                LoanAmountError = _localizationService.GetString("LoanAmountRequired");
                isValid = false;
            }
            else
            {
                var asciiLoan = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(LoanAmountText.Trim());
                if (!decimal.TryParse(asciiLoan, NumberStyles.Any, CultureInfo.InvariantCulture, out var loanAmountVal) || loanAmountVal <= 0m)
                {
                    LoanAmountError = _localizationService.GetString("InvalidLoanAmount");
                    isValid = false;
                }
            }

            if (!LoanDate.HasValue)
            {
                LoanDateError = _localizationService.GetString("LoanDateRequired");
                isValid = false;
            }
        }

        if (string.IsNullOrWhiteSpace(InterestRateText))
        {
            InterestRateError = _localizationService.GetString("InterestRateRequired");
            isValid = false;
        }
        else
        {
            var asciiRate = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(InterestRateText.Trim());
            if (!decimal.TryParse(asciiRate, NumberStyles.Any, CultureInfo.InvariantCulture, out var interestRateVal) || interestRateVal <= 0m)
            {
                InterestRateError = _localizationService.GetString("InvalidInterestRate");
                isValid = false;
            }
        }

        if (!IsValidMobileNumber(MobileNumber))
        {
            MobileNumberError = _localizationService.GetString("InvalidMobileNumber");
            isValid = false;
        }

        if (!IsValidAadharNumber(AadharNumber))
        {
            AadharNumberError = _localizationService.GetString("InvalidAadharNumber");
            isValid = false;
        }

        return isValid;
    }

    private static bool IsValidMobileNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var ascii = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(value);
        var digits = new string(ascii.Where(char.IsDigit).ToArray());
        return digits.Length == 10 && digits[0] >= '6' && digits[0] <= '9';
    }

    private static bool IsValidAadharNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var ascii = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(value);
        var digits = new string(ascii.Where(char.IsDigit).ToArray());
        return digits.Length == 12;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        NotifyInputModeProperties();
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(FullNameLabel));
        OnPropertyChanged(nameof(FullNamePlaceholder));
        OnPropertyChanged(nameof(NameLabel));
        OnPropertyChanged(nameof(VillageLabel));
        OnPropertyChanged(nameof(VillagePlaceholder));
        OnPropertyChanged(nameof(MobileNumberLabel));
        OnPropertyChanged(nameof(MobileNumberPlaceholder));
        OnPropertyChanged(nameof(AadharNumberLabel));
        OnPropertyChanged(nameof(AadharNumberPlaceholder));
        OnPropertyChanged(nameof(BorrowerNumberLabel));
        OnPropertyChanged(nameof(BorrowerNumberPlaceholder));
        OnPropertyChanged(nameof(LoanTypeLabel));
        OnPropertyChanged(nameof(SelectLoanTypeLabel));
        OnPropertyChanged(nameof(OrnamentTypeLabel));
        OnPropertyChanged(nameof(SelectOrnamentCategoryLabel));
        OnPropertyChanged(nameof(OrnamentWeightLabel));
        OnPropertyChanged(nameof(GramsLabel));
        OnPropertyChanged(nameof(EnterOrnamentTypeLabel));
        OnPropertyChanged(nameof(EnterOrnamentTypePlaceholderLabel));
        OnPropertyChanged(nameof(LoanAmountLabel));
        OnPropertyChanged(nameof(LoanAmountPlaceholder));
        OnPropertyChanged(nameof(LoanDateLabel));
        OnPropertyChanged(nameof(InterestRateLabel));
        OnPropertyChanged(nameof(InterestRatePlaceholder));
        OnPropertyChanged(nameof(PercentPerMonthLabel));
        OnPropertyChanged(nameof(BorrowerPhotoLabel));
        OnPropertyChanged(nameof(NoBorrowerPhotoCapturedLabel));
        OnPropertyChanged(nameof(TakePhotoLabel));
        OnPropertyChanged(nameof(RetakePhotoLabel));
        OnPropertyChanged(nameof(RemovePhotoLabel));
        OnPropertyChanged(nameof(OrnamentPhotoLabel));
        OnPropertyChanged(nameof(GoldSilverLabel));
        OnPropertyChanged(nameof(NoOrnamentPhotoCapturedLabel));
        OnPropertyChanged(nameof(OptionalLabel));
        OnPropertyChanged(nameof(CancelText));
        OnPropertyChanged(nameof(SaveBorrowerText));
        OnPropertyChanged(nameof(LoanTypeOptions));
        OnPropertyChanged(nameof(OrnamentTypeOptions));
        OnPropertyChanged(nameof(ReopenAccountLabel));
        OnPropertyChanged(nameof(ConfirmReopenAccountTitleLabel));
        OnPropertyChanged(nameof(ConfirmReopenAccountMessageLabel));
        OnPropertyChanged(nameof(ClosedStatusLabel));
        OnPropertyChanged(nameof(ClosedAccountBannerTitle));
        OnPropertyChanged(nameof(ClosedAccountBannerDescription));

        // Refresh localized display for numeric fields (storage remains ASCII, getter localizes)
        OnPropertyChanged(nameof(MobileNumber));
        OnPropertyChanged(nameof(AadharNumber));
        OnPropertyChanged(nameof(BorrowerNumber));
        OnPropertyChanged(nameof(OrnamentWeightText));
        OnPropertyChanged(nameof(LoanAmountText));
        OnPropertyChanged(nameof(InterestRateText));

        if (!IsNew && BorrowerId != Guid.Empty && !IsSaving)
        {
            _ = LoadBorrowerAsync(BorrowerId);
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Desktop.Configuration;
using DhirDhar.Desktop.ViewModels;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.ViewModels.Borrowers;

public sealed class BorrowersViewModel : ViewModelBase
{
    private readonly IBorrowerService _borrowerService;
    private readonly ILocalizationService _localizationService;
    private readonly ITranslationService _translationService;
    private readonly ILogger<BorrowersViewModel> _logger;

    private ObservableCollection<BorrowerSummary> _borrowers = new();
    private BorrowerFilter _currentFilter = BorrowerFilter.All;
    private string _searchTerm = string.Empty;
    private bool _isLoading;
    private bool _hasError;
    private bool _isEmpty;
    private bool _isNoResults;
    private string _errorMessage = string.Empty;
    private DateTime _lastUpdated;

    public BorrowersViewModel(
        IBorrowerService borrowerService,
        ILocalizationService localizationService,
        ITranslationService translationService,
        AppOptions appOptions,
        ILogger<BorrowersViewModel> logger)
    {
        _borrowerService = borrowerService;
        _localizationService = localizationService;
        _translationService = translationService;
        _logger = logger;
        ApplicationVersion = appOptions.Version;

        _localizationService.LanguageChanged += OnLanguageChanged;

        LoadCommand = new RelayCommand(async () => await LoadAsync());
        RefreshCommand = new RelayCommand(async () => await LoadAsync());
        SearchCommand = new RelayCommand(async () => await SearchAsync());
        RetryCommand = new RelayCommand(async () => await LoadAsync());
        AddBorrowerCommand = new RelayCommand(OpenNewBorrower);
        EditBorrowerCommand = new RelayCommand<Guid>(OpenBorrowerForEdit);
        SelectBorrowerCommand = new RelayCommand<Guid>(OnSelectBorrower);
        FilterAllCommand = new RelayCommand(() => { CurrentFilter = BorrowerFilter.All; _ = LoadAsync(); });
        FilterActiveCommand = new RelayCommand(() => { CurrentFilter = BorrowerFilter.Active; _ = LoadAsync(); });
        FilterClosedCommand = new RelayCommand(() => { CurrentFilter = BorrowerFilter.Closed; _ = LoadAsync(); });
        ScanQrCommand = new RelayCommand(async () => { if (RequestScanQr != null) await RequestScanQr(); });
    }

    public string ApplicationVersion { get; }

    public Func<Task>? RequestScanQr { get; set; }
    internal Func<BorrowerEditViewModel>? BorrowerEditViewModelFactory { get; set; }
    internal Action<BorrowerEditViewModel>? BorrowerEditNavigationRequested { get; set; }
    internal Action<Guid>? BorrowerDetailsNavigationRequested { get; set; }

    public RelayCommand<Guid> SelectBorrowerCommand { get; }
    public RelayCommand ScanQrCommand { get; }

    public ObservableCollection<BorrowerSummary> Borrowers
    {
        get => _borrowers;
        private set => SetProperty(ref _borrowers, value);
    }

    public BorrowerFilter CurrentFilter
    {
        get => _currentFilter;
        private set => SetProperty(ref _currentFilter, value);
    }

    private System.Threading.CancellationTokenSource? _searchDebounceCts;

    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (SetProperty(ref _searchTerm, value))
            {
                TriggerDebouncedSearch();
            }
        }
    }

    private void TriggerDebouncedSearch()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new System.Threading.CancellationTokenSource();
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
                await LoadAsync();
            }
            catch (OperationCanceledException)
            {
                // Superseded by newer search request
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

    public bool IsEmpty
    {
        get => _isEmpty;
        private set
        {
            if (SetProperty(ref _isEmpty, value))
            {
                OnPropertyChanged(nameof(HasItems));
            }
        }
    }

    public bool IsNoResults
    {
        get => _isNoResults;
        private set
        {
            if (SetProperty(ref _isNoResults, value))
            {
                OnPropertyChanged(nameof(HasItems));
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public DateTime LastUpdated
    {
        get => _lastUpdated;
        private set => SetProperty(ref _lastUpdated, value);
    }

    public bool HasItems => Borrowers.Count > 0;

    public string PageTitle => _localizationService.GetString("Borrowers");
    public string PageSubtitle => _localizationService.GetString("BorrowersSubtitle");
    public string SearchPlaceholder => _localizationService.GetString("SearchBorrowersPlaceholder");
    public string SearchText => _localizationService.GetString("Search");
    public string RefreshText => _localizationService.GetString("Refresh");
    public string ScanQrText => _localizationService.GetString("ScanQr");
    public string AddBorrowerText => _localizationService.GetString("AddBorrower");
    public string EditText => _localizationService.GetString("Edit");
    public string RetryText => _localizationService.GetString("Retry");
    public string AllFilterText => _localizationService.GetString("All");
    public string ActiveFilterText => _localizationService.GetString("Active");
    public string ClosedFilterText => _localizationService.GetString("Closed");
    public string ArchivedFilterText => ClosedFilterText;
    public string BorrowerNumberColumnHeader => _localizationService.GetString("BorrowerNumberColumn");
    public string BorrowerNameColumnHeader => _localizationService.GetString("BorrowerNameColumn");
    public string ContactColumnHeader => _localizationService.GetString("ContactColumn");
    public string OutstandingColumnHeader => _localizationService.GetString("OutstandingColumn");
    public string StatusColumnHeader => _localizationService.GetString("StatusColumn");
    public string LastActivityColumnHeader => _localizationService.GetString("LastActivityColumn");
    public string NoBorrowersTitle => _localizationService.GetString("NoBorrowersTitle");
    public string NoBorrowersDescription => _localizationService.GetString("NoBorrowersDescription");
    public string NoSearchResultsTitle => _localizationService.GetString("NoSearchResultsTitle");
    public string NoSearchResultsDescription => _localizationService.GetString("NoSearchResultsDescription");
    public string ReadyText => _localizationService.GetString("Ready");
    public string ViewBorrowerDetailsLabel => _localizationService.GetString("ViewBorrowerDetails");

    public string FormattedApplicationVersion => $"{L("Version")} {LocalizeDigits(ApplicationVersion)}";

    public string FormattedFinancialYear
    {
        get
        {
            var now = DateTime.Now;
            var year = now.Month >= 4 ? now.Year : now.Year - 1;
            return LocalizeDigits($"{L("FinancialYearPrefix")} {year}-{((year + 1) % 100):D2}");
        }
    }

    public string FormattedLastUpdated => LastUpdated == DateTime.MinValue ? string.Empty : LDateTime(LastUpdated);

    public RelayCommand LoadCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SearchCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand AddBorrowerCommand { get; }
    public RelayCommand<Guid> EditBorrowerCommand { get; }
    public RelayCommand FilterAllCommand { get; }
    public RelayCommand FilterActiveCommand { get; }
    public RelayCommand FilterClosedCommand { get; }
    public RelayCommand FilterArchivedCommand => FilterClosedCommand;

    private System.Threading.CancellationTokenSource? _loadCts;

    public async Task LoadAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new System.Threading.CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        HasError = false;
        IsEmpty = false;
        IsNoResults = false;
        ErrorMessage = string.Empty;

        try
        {
            var filter = CurrentFilter;
            var term = SearchTerm;
            var result = await _borrowerService.GetListAsync(filter, term, 1, 0, ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested) return;

            var currentLang = _localizationService.CurrentLanguage;
            var items = result.Items.Localize(_translationService, currentLang).ToList();

            if (ct.IsCancellationRequested) return;

            Borrowers = new ObservableCollection<BorrowerSummary>(items);

            if (result.TotalCount == 0)
            {
                if (string.IsNullOrWhiteSpace(term))
                {
                    IsEmpty = true;
                }
                else
                {
                    IsNoResults = true;
                }
            }

            LastUpdated = DateTime.Now;
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(FormattedLastUpdated));

            _logger.LogInformation("Borrowers loaded. Count={Count}, Filter={Filter}.", result.TotalCount, filter);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation when newer search/filter was triggered
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load borrowers. Operation: {Operation}", nameof(LoadAsync));
            HasError = true;
            ErrorMessage = _localizationService.GetString("BorrowersLoadFailed");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    private async Task SearchAsync()
    {
        await LoadAsync();
    }

    private void OpenNewBorrower()
    {
        var vm = BorrowerEditViewModelFactory?.Invoke();
        if (vm is null)
        {
            return;
        }

        vm.SetAsNew();
        BorrowerEditNavigationRequested?.Invoke(vm);
    }

    private void OpenBorrowerForEdit(Guid id)
    {
        var vm = BorrowerEditViewModelFactory?.Invoke();
        if (vm is null)
        {
            return;
        }

        vm.SetForEdit(id);
        BorrowerEditNavigationRequested?.Invoke(vm);
    }

    private void OnSelectBorrower(Guid id)
    {
        if (id != Guid.Empty)
        {
            BorrowerDetailsNavigationRequested?.Invoke(id);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(RefreshText));
        OnPropertyChanged(nameof(ScanQrText));
        OnPropertyChanged(nameof(AddBorrowerText));
        OnPropertyChanged(nameof(EditText));
        OnPropertyChanged(nameof(RetryText));
        OnPropertyChanged(nameof(AllFilterText));
        OnPropertyChanged(nameof(ActiveFilterText));
        OnPropertyChanged(nameof(ClosedFilterText));
        OnPropertyChanged(nameof(BorrowerNumberColumnHeader));
        OnPropertyChanged(nameof(BorrowerNameColumnHeader));
        OnPropertyChanged(nameof(ContactColumnHeader));
        OnPropertyChanged(nameof(OutstandingColumnHeader));
        OnPropertyChanged(nameof(StatusColumnHeader));
        OnPropertyChanged(nameof(LastActivityColumnHeader));
        OnPropertyChanged(nameof(NoBorrowersTitle));
        OnPropertyChanged(nameof(NoBorrowersDescription));
        OnPropertyChanged(nameof(NoSearchResultsTitle));
        OnPropertyChanged(nameof(NoSearchResultsDescription));
        OnPropertyChanged(nameof(ReadyText));
        _ = LoadAsync();
    }
}

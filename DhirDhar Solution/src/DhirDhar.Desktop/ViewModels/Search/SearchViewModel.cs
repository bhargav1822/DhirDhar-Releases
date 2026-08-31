using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Search;
using DhirDhar.Application.Search.Models;
using DhirDhar.Desktop.ViewModels;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.ViewModels.Search;

public sealed record SearchCategoryOption(string Value, string Label);

public sealed class SearchViewModel : ViewModelBase
{
    private readonly ISearchService _searchService;
    private readonly ILocalizationService _localizationService;
    private readonly ITranslationService _translationService;
    private readonly ILogger<SearchViewModel> _logger;

    private string _searchTerm = string.Empty;
    private string _selectedCategory = "All";
    private ObservableCollection<SearchResult> _results = new();
    private bool _isSearching;
    private bool _hasResults;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private int _totalCount;

    public SearchViewModel(
        ISearchService searchService,
        ILocalizationService localizationService,
        ITranslationService translationService,
        ILogger<SearchViewModel> logger)
    {
        _searchService = searchService;
        _localizationService = localizationService;
        _translationService = translationService;
        _logger = logger;

        AttachLocalization(localizationService);

        _localizationService.LanguageChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(CategoryOptions));
            if (!string.IsNullOrWhiteSpace(SearchTerm) && HasResults)
            {
                _ = SearchAsync();
            }
        };

        SearchCommand = new RelayCommand(async () => await SearchAsync());
        ClearFiltersCommand = new RelayCommand(ClearFilters);
    }

    private System.Threading.CancellationTokenSource? _searchCts;
    private bool _hasNoResults;

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

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                TriggerDebouncedSearch();
            }
        }
    }

    public ObservableCollection<SearchResult> Results
    {
        get => _results;
        private set => SetProperty(ref _results, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetProperty(ref _isSearching, value))
            {
                OnPropertyChanged(nameof(HasNoResults));
            }
        }
    }

    public bool HasResults
    {
        get => _hasResults;
        private set
        {
            if (SetProperty(ref _hasResults, value))
            {
                OnPropertyChanged(nameof(HasNoResults));
            }
        }
    }

    public bool HasNoResults
    {
        get => !IsSearching && !HasError && !string.IsNullOrWhiteSpace(SearchTerm) && Results.Count == 0;
        private set => SetProperty(ref _hasNoResults, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
            {
                OnPropertyChanged(nameof(HasNoResults));
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(HasNoResults));
            }
        }
    }

    public string PageTitle => L("Search");
    public string SearchPlaceholderLabel => L("SearchAllPlaceholder");
    public string SearchButtonLabel => L("Search");
    public string ClearButtonLabel => L("Clear");
    public string RetryLabel => L("Retry");
    public string NoResultsFoundLabel => L("NoResultsFound") ?? "No results found";
    public string NoResultsFoundSubtitle => L("NoResultsFoundSubtitle") ?? "Try searching with a different name, number, or keyword.";

    public ObservableCollection<SearchCategoryOption> CategoryOptions => new()
    {
        new("All", L("All")),
        new("Borrower", L("Borrower")),
        new("Transaction", L("Transaction"))
    };

    public RelayCommand SearchCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }

    private void TriggerDebouncedSearch()
    {
        _searchCts?.Cancel();
        _searchCts = new System.Threading.CancellationTokenSource();
        var ct = _searchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(SearchTerm))
                {
                    await Task.Delay(150, ct);
                }
                if (ct.IsCancellationRequested) return;
                await SearchAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Debounced request superseded
            }
        });
    }

    public async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new System.Threading.CancellationTokenSource();
        await SearchAsync(_searchCts.Token);
    }

    public async Task SearchAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SearchTerm))
        {
            Results.Clear();
            TotalCount = 0;
            HasResults = false;
            HasNoResults = false;
            IsSearching = false;
            return;
        }

        IsSearching = true;
        HasError = false;
        HasResults = false;

        try
        {
            var filter = new SearchFilter(
                SearchTerm,
                SelectedCategory,
                null,
                null,
                null,
                null,
                null,
                "Date",
                true,
                1,
                100);

            var result = await _searchService.SearchAsync(filter, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested) return;

            var items = System.Linq.Enumerable.Select(result.Items, item => new SearchResult(
                item.EntityType,
                item.Id,
                item.Title,
                item.Subtitle,
                item.Status,
                item.Date,
                item.Amount));

            Results = new ObservableCollection<SearchResult>(items);
            TotalCount = result.TotalCount;
            HasResults = result.Items.Count > 0;
            OnPropertyChanged(nameof(HasNoResults));
        }
        catch (OperationCanceledException)
        {
            // Ignore cancelled search
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed.");
            HasError = true;
            ErrorMessage = L("SearchFailed");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsSearching = false;
                OnPropertyChanged(nameof(HasNoResults));
            }
        }
    }

    private void ClearFilters()
    {
        _searchCts?.Cancel();
        _searchTerm = string.Empty;
        _selectedCategory = "All";
        Results = new ObservableCollection<SearchResult>();
        HasResults = false;
        TotalCount = 0;
        HasError = false;
        ErrorMessage = string.Empty;
        IsSearching = false;
        OnPropertyChanged(nameof(SearchTerm));
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(HasNoResults));
    }
}

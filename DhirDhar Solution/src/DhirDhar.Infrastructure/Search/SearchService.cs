using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Search;
using DhirDhar.Application.Search.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using DhirDhar.Application.Caching;

namespace DhirDhar.Infrastructure.Search;

public sealed class SearchService : ISearchService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SearchService> _logger;
    private readonly ICacheService? _cacheService;

    public SearchService(
        IServiceScopeFactory scopeFactory,
        ILogger<SearchService> logger,
        ICacheService? cacheService = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cacheService = cacheService ?? scopeFactory.CreateScope().ServiceProvider.GetService<ICacheService>();
    }

    public async Task<SearchResultPage> SearchAsync(SearchFilter filter, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"search_query_{filter.SearchTerm?.Trim().ToLower()}_{filter.BorrowerFilter}_{filter.Page}_{filter.PageSize}";
        if (_cacheService != null && !filter.StartDate.HasValue && !filter.EndDate.HasValue)
        {
            var cached = _cacheService.Get<SearchResultPage>(cacheKey);
            if (cached != null)
            {
                return cached;
            }
        }
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDhar.Infrastructure.Persistence.DhirDharDbContext>();

        var includeBorrowers = !string.Equals(filter.BorrowerFilter, "Transaction", StringComparison.OrdinalIgnoreCase);
        var includeTransactions = string.IsNullOrEmpty(filter.BorrowerFilter) ||
                                  string.Equals(filter.BorrowerFilter, "All", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(filter.BorrowerFilter, "Transaction", StringComparison.OrdinalIgnoreCase);

        var localizationService = scope.ServiceProvider.GetService<DhirDhar.Application.Localization.ILocalizationService>();
        var translationService = scope.ServiceProvider.GetService<ITranslationService>();
        var currentLang = localizationService != null
            ? localizationService.CurrentLanguage
            : (!string.IsNullOrWhiteSpace(filter.SearchTerm) ? ScriptTranslator.DetectLanguage(filter.SearchTerm) : "gu-IN");

        var borrowers = new List<SearchResult>();
        var transactions = new List<SearchResult>();

        string rawTerm = string.Empty;
        string term = string.Empty;
        string englishTerm = string.Empty;
        string gujaratiTerm = string.Empty;
        string hindiTerm = string.Empty;
        string asciiDigits = string.Empty;
        List<string> matchingTranslatedSources = new();

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            rawTerm = filter.SearchTerm.Trim();
            if (rawTerm.StartsWith("DHIRDHAR|ACCOUNT|", StringComparison.OrdinalIgnoreCase))
            {
                rawTerm = rawTerm.Substring("DHIRDHAR|ACCOUNT|".Length).Trim();
            }
            else if (rawTerm.StartsWith("DHIRDHAR|", StringComparison.OrdinalIgnoreCase))
            {
                var parts = rawTerm.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 3) rawTerm = parts[2];
            }
            rawTerm = rawTerm.TrimStart('#').Trim();
            term = rawTerm.ToLowerInvariant();
            englishTerm = ScriptTranslator.ToEnglish(rawTerm).Trim().ToLowerInvariant();
            gujaratiTerm = ScriptTranslator.ToGujarati(rawTerm).Trim();
            hindiTerm = ScriptTranslator.ToHindi(rawTerm).Trim();
            asciiDigits = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(rawTerm);

            matchingTranslatedSources = await dbContext.UserTextTranslations
                .AsNoTracking()
                .Where(t => EF.Functions.Like(t.TranslatedText, $"%{rawTerm}%") || EF.Functions.Like(t.TranslatedText.ToLower(), $"%{term}%"))
                .OrderBy(t => t.SourceText)
                .Select(t => t.SourceText)
                .Distinct()
                .Take(50)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (includeBorrowers)
        {
            var borrowerQuery = dbContext.Borrowers.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(rawTerm))
            {
                borrowerQuery = borrowerQuery.Where(b =>
                    EF.Functions.Like(b.Name, $"%{rawTerm}%") ||
                    EF.Functions.Like(b.Name.ToLower(), $"%{term}%") ||
                    matchingTranslatedSources.Contains(b.Name) ||
                    (!string.IsNullOrEmpty(englishTerm) && (EF.Functions.Like(b.Name, $"%{englishTerm}%") || EF.Functions.Like(b.Name.ToLower(), $"%{englishTerm}%"))) ||
                    (!string.IsNullOrEmpty(gujaratiTerm) && EF.Functions.Like(b.Name, $"%{gujaratiTerm}%")) ||
                    (!string.IsNullOrEmpty(hindiTerm) && EF.Functions.Like(b.Name, $"%{hindiTerm}%")) ||
                    (b.FatherName != null && (
                        EF.Functions.Like(b.FatherName, $"%{rawTerm}%") ||
                        EF.Functions.Like(b.FatherName.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(b.FatherName.ToLower(), $"%{englishTerm}%") ||
                        EF.Functions.Like(b.FatherName, $"%{gujaratiTerm}%") ||
                        EF.Functions.Like(b.FatherName, $"%{hindiTerm}%") ||
                        matchingTranslatedSources.Contains(b.FatherName))) ||
                    (b.Surname != null && (
                        EF.Functions.Like(b.Surname, $"%{rawTerm}%") ||
                        EF.Functions.Like(b.Surname.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(b.Surname.ToLower(), $"%{englishTerm}%") ||
                        EF.Functions.Like(b.Surname, $"%{gujaratiTerm}%") ||
                        EF.Functions.Like(b.Surname, $"%{hindiTerm}%") ||
                        matchingTranslatedSources.Contains(b.Surname))) ||
                    (b.Village != null && (
                        EF.Functions.Like(b.Village, $"%{rawTerm}%") ||
                        EF.Functions.Like(b.Village.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(b.Village.ToLower(), $"%{englishTerm}%") ||
                        EF.Functions.Like(b.Village, $"%{gujaratiTerm}%") ||
                        EF.Functions.Like(b.Village, $"%{hindiTerm}%") ||
                        matchingTranslatedSources.Contains(b.Village))) ||
                    (b.Address != null && (
                        EF.Functions.Like(b.Address, $"%{rawTerm}%") ||
                        EF.Functions.Like(b.Address.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(b.Address.ToLower(), $"%{englishTerm}%") ||
                        EF.Functions.Like(b.Address, $"%{gujaratiTerm}%"))) ||
                    (b.Notes != null && (
                        EF.Functions.Like(b.Notes, $"%{rawTerm}%") ||
                        EF.Functions.Like(b.Notes.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(b.Notes.ToLower(), $"%{englishTerm}%") ||
                        EF.Functions.Like(b.Notes, $"%{gujaratiTerm}%"))) ||
                    EF.Functions.Like(b.BorrowerNumber.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(b.BorrowerNumber.ToLower(), $"%{englishTerm}%") ||
                    EF.Functions.Like(b.BorrowerNumber.ToLower(), $"%{asciiDigits}%") ||
                    (b.Phone != null && (
                        EF.Functions.Like(b.Phone.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(b.Phone.ToLower(), $"%{asciiDigits}%"))) ||
                    (b.AadharNumber != null && (
                        EF.Functions.Like(b.AadharNumber.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(b.AadharNumber.ToLower(), $"%{asciiDigits}%"))));
            }

            if (!string.IsNullOrEmpty(filter.BorrowerFilter) &&
                !string.Equals(filter.BorrowerFilter, "All", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(filter.BorrowerFilter, "Borrower", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(filter.BorrowerFilter, "Transaction", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse<BorrowerStatus>(filter.BorrowerFilter, out var status))
                {
                    borrowerQuery = borrowerQuery.Where(b => b.Status == status);
                }
            }

            var limit = filter.PageSize > 0 ? filter.PageSize : 500;
            var rawBorrowers = await borrowerQuery
                .OrderByDescending(b => b.CreatedAt)
                .Take(limit)
                .Select(b => new
                {
                    b.Id,
                    b.Name,
                    b.BorrowerNumber,
                    b.FatherName,
                    b.Surname,
                    b.Village,
                    b.Status,
                    b.EntryDate
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            borrowers = rawBorrowers.Select(b =>
            {
                var localizedName = translationService != null ? translationService.Translate(b.Name, currentLang) : ScriptTranslator.Translate(b.Name, currentLang);
                var localizedFather = !string.IsNullOrWhiteSpace(b.FatherName) ? (translationService != null ? translationService.Translate(b.FatherName, currentLang) : ScriptTranslator.Translate(b.FatherName, currentLang)) : null;
                var localizedSurname = !string.IsNullOrWhiteSpace(b.Surname) ? (translationService != null ? translationService.Translate(b.Surname, currentLang) : ScriptTranslator.Translate(b.Surname, currentLang)) : null;
                var localizedVillage = !string.IsNullOrWhiteSpace(b.Village) ? (translationService != null ? translationService.Translate(b.Village, currentLang) : ScriptTranslator.Translate(b.Village, currentLang)) : null;

                var localizedBorrowerNumber = !string.IsNullOrWhiteSpace(b.BorrowerNumber)
                    ? (localizationService != null ? localizationService.LocalizeDigits(b.BorrowerNumber) : ScriptTranslator.ConvertDigitsToIndic(b.BorrowerNumber, currentLang))
                    : string.Empty;

                var sonOfTemplate = localizationService?.GetString("SonOf") ?? "S/o {0}";
                if (!sonOfTemplate.Contains("{0}")) sonOfTemplate = "S/o {0}";

                var details = new List<string>();
                if (!string.IsNullOrWhiteSpace(localizedBorrowerNumber)) details.Add(localizedBorrowerNumber);
                if (!string.IsNullOrWhiteSpace(localizedFather)) details.Add(string.Format(sonOfTemplate, localizedFather));
                else if (!string.IsNullOrWhiteSpace(localizedSurname)) details.Add(localizedSurname);
                if (!string.IsNullOrWhiteSpace(localizedVillage)) details.Add(localizedVillage);

                var subtitle = details.Count > 0 ? string.Join(" • ", details) : localizedBorrowerNumber;
                var localizedStatus = localizationService != null ? localizationService.GetString(b.Status.ToString()) : b.Status.ToString();
                return new SearchResult("Borrower", b.Id.ToString(), localizedName, subtitle, localizedStatus, b.EntryDate, null);
            }).ToList();
        }

        if (includeTransactions)
        {
            var transactionQuery = dbContext.Transactions.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(rawTerm))
            {
                transactionQuery = transactionQuery.Where(t =>
                    (t.Reference != null && (
                        EF.Functions.Like(t.Reference, $"%{rawTerm}%") ||
                        EF.Functions.Like(t.Reference.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(t.Reference.ToLower(), $"%{englishTerm}%") ||
                        EF.Functions.Like(t.Reference.ToLower(), $"%{asciiDigits}%"))) ||
                    (t.Description != null && (
                        EF.Functions.Like(t.Description, $"%{rawTerm}%") ||
                        EF.Functions.Like(t.Description.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(t.Description.ToLower(), $"%{englishTerm}%") ||
                        EF.Functions.Like(t.Description, $"%{gujaratiTerm}%") ||
                        EF.Functions.Like(t.Description, $"%{hindiTerm}%"))) ||
                    (t.Borrower != null && (
                        EF.Functions.Like(t.Borrower.Name, $"%{rawTerm}%") ||
                        EF.Functions.Like(t.Borrower.Name.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(t.Borrower.Name.ToLower(), $"%{englishTerm}%") ||
                        EF.Functions.Like(t.Borrower.Name, $"%{gujaratiTerm}%") ||
                        EF.Functions.Like(t.Borrower.Name, $"%{hindiTerm}%") ||
                        EF.Functions.Like(t.Borrower.BorrowerNumber.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(t.Borrower.BorrowerNumber.ToLower(), $"%{englishTerm}%") ||
                        EF.Functions.Like(t.Borrower.BorrowerNumber.ToLower(), $"%{asciiDigits}%") ||
                        (t.Borrower.Phone != null && (
                            EF.Functions.Like(t.Borrower.Phone.ToLower(), $"%{term}%") ||
                            EF.Functions.Like(t.Borrower.Phone.ToLower(), $"%{asciiDigits}%"))) ||
                        (t.Borrower.Village != null && (
                            EF.Functions.Like(t.Borrower.Village.ToLower(), $"%{term}%") ||
                            EF.Functions.Like(t.Borrower.Village.ToLower(), $"%{englishTerm}%") ||
                            EF.Functions.Like(t.Borrower.Village, $"%{gujaratiTerm}%") ||
                            EF.Functions.Like(t.Borrower.Village, $"%{hindiTerm}%"))))));
            }

            if (filter.StartDate.HasValue)
            {
                transactionQuery = transactionQuery.Where(t => t.OccurredOn >= filter.StartDate.Value);
            }

            if (filter.EndDate.HasValue)
            {
                transactionQuery = transactionQuery.Where(t => t.OccurredOn <= filter.EndDate.Value);
            }

            var limit = filter.PageSize > 0 ? filter.PageSize : 500;
            var rawTransactions = await transactionQuery
                .Include(t => t.Borrower)
                .OrderByDescending(t => t.OccurredOn)
                .Take(limit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            transactions = rawTransactions.Select(t =>
            {
                var typeKey = t.Type.ToString();
                var localizedType = localizationService != null ? localizationService.GetString(typeKey) : typeKey;
                var bName = t.Borrower != null && !string.IsNullOrEmpty(t.Borrower.Name)
                    ? (translationService != null ? translationService.Translate(t.Borrower.Name, currentLang) : ScriptTranslator.Translate(t.Borrower.Name, currentLang))
                    : string.Empty;
                var bNum = t.Borrower != null && !string.IsNullOrEmpty(t.Borrower.BorrowerNumber)
                    ? (localizationService != null ? localizationService.LocalizeDigits(t.Borrower.BorrowerNumber) : ScriptTranslator.ConvertDigitsToIndic(t.Borrower.BorrowerNumber, currentLang))
                    : string.Empty;
                var subtitle = !string.IsNullOrEmpty(bName)
                    ? (!string.IsNullOrEmpty(bNum) ? $"{bName} (#{bNum})" : bName)
                    : (t.Reference ?? string.Empty);
                var localizedDesc = !string.IsNullOrWhiteSpace(t.Description)
                    ? (translationService != null ? translationService.Translate(t.Description, currentLang) : ScriptTranslator.Translate(t.Description, currentLang))
                    : (t.Description ?? string.Empty);

                return new SearchResult(
                    "Transaction",
                    t.BorrowerId.HasValue ? t.BorrowerId.Value.ToString() : t.Id.ToString(),
                    localizedType,
                    subtitle,
                    localizedDesc,
                    t.OccurredOn,
                    t.Amount.Amount);
            }).ToList();
        }

        var allResults = borrowers.Concat(transactions)
            .OrderByDescending(r => r.Date)
            .ToList();

        var pageResult = new SearchResultPage(allResults, allResults.Count, filter.Page, filter.PageSize);
        if (_cacheService != null && !filter.StartDate.HasValue && !filter.EndDate.HasValue)
        {
            _cacheService.Set(cacheKey, pageResult, slidingExpiration: TimeSpan.FromSeconds(30), absoluteExpiration: TimeSpan.FromMinutes(2));
        }

        return pageResult;
    }

    public async Task<IReadOnlyList<BorrowerSearchResult>> SearchBorrowersAsync(
        string? searchTerm,
        string? statusFilter,
        DateTime? entryDateFrom,
        DateTime? entryDateTo,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDhar.Infrastructure.Persistence.DhirDharDbContext>();
        var localizationService = scope.ServiceProvider.GetService<DhirDhar.Application.Localization.ILocalizationService>();
        var translationService = scope.ServiceProvider.GetService<ITranslationService>();
        var currentLang = localizationService != null
            ? localizationService.CurrentLanguage
            : (!string.IsNullOrWhiteSpace(searchTerm) ? ScriptTranslator.DetectLanguage(searchTerm) : "gu-IN");

        var query = dbContext.Borrowers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var rawTerm = searchTerm.Trim();
            if (rawTerm.StartsWith("DHIRDHAR|ACCOUNT|", StringComparison.OrdinalIgnoreCase))
            {
                rawTerm = rawTerm.Substring("DHIRDHAR|ACCOUNT|".Length).Trim();
            }
            else if (rawTerm.StartsWith("DHIRDHAR|", StringComparison.OrdinalIgnoreCase))
            {
                var parts = rawTerm.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 3) rawTerm = parts[2];
            }
            rawTerm = rawTerm.TrimStart('#').Trim();
            var term = rawTerm.ToLowerInvariant();
            var englishTerm = ScriptTranslator.ToEnglish(rawTerm).Trim().ToLowerInvariant();
            var gujaratiTerm = ScriptTranslator.ToGujarati(rawTerm).Trim();
            var hindiTerm = ScriptTranslator.ToHindi(rawTerm).Trim();
            var asciiDigits = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(rawTerm);

            query = query.Where(b =>
                EF.Functions.Like(b.Name, $"%{rawTerm}%") ||
                EF.Functions.Like(b.Name.ToLower(), $"%{term}%") ||
                (!string.IsNullOrEmpty(englishTerm) && (EF.Functions.Like(b.Name.ToLower(), $"%{englishTerm}%") || EF.Functions.Like(b.Name, $"%{englishTerm}%"))) ||
                (!string.IsNullOrEmpty(gujaratiTerm) && EF.Functions.Like(b.Name, $"%{gujaratiTerm}%")) ||
                (!string.IsNullOrEmpty(hindiTerm) && EF.Functions.Like(b.Name, $"%{hindiTerm}%")) ||
                (b.FatherName != null && (
                    EF.Functions.Like(b.FatherName, $"%{rawTerm}%") ||
                    EF.Functions.Like(b.FatherName.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(b.FatherName.ToLower(), $"%{englishTerm}%") ||
                    EF.Functions.Like(b.FatherName, $"%{gujaratiTerm}%") ||
                    EF.Functions.Like(b.FatherName, $"%{hindiTerm}%"))) ||
                (b.Surname != null && (
                    EF.Functions.Like(b.Surname, $"%{rawTerm}%") ||
                    EF.Functions.Like(b.Surname.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(b.Surname.ToLower(), $"%{englishTerm}%") ||
                    EF.Functions.Like(b.Surname, $"%{gujaratiTerm}%") ||
                    EF.Functions.Like(b.Surname, $"%{hindiTerm}%"))) ||
                (b.Village != null && (
                    EF.Functions.Like(b.Village, $"%{rawTerm}%") ||
                    EF.Functions.Like(b.Village.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(b.Village.ToLower(), $"%{englishTerm}%") ||
                    EF.Functions.Like(b.Village, $"%{gujaratiTerm}%") ||
                    EF.Functions.Like(b.Village, $"%{hindiTerm}%"))) ||
                (b.Address != null && (
                    EF.Functions.Like(b.Address, $"%{rawTerm}%") ||
                    EF.Functions.Like(b.Address.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(b.Address.ToLower(), $"%{englishTerm}%") ||
                    EF.Functions.Like(b.Address, $"%{gujaratiTerm}%"))) ||
                (b.Notes != null && (
                    EF.Functions.Like(b.Notes, $"%{rawTerm}%") ||
                    EF.Functions.Like(b.Notes.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(b.Notes.ToLower(), $"%{englishTerm}%") ||
                    EF.Functions.Like(b.Notes, $"%{gujaratiTerm}%"))) ||
                EF.Functions.Like(b.BorrowerNumber.ToLower(), $"%{term}%") ||
                EF.Functions.Like(b.BorrowerNumber.ToLower(), $"%{englishTerm}%") ||
                EF.Functions.Like(b.BorrowerNumber.ToLower(), $"%{asciiDigits}%") ||
                (b.Phone != null && (
                    EF.Functions.Like(b.Phone.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(b.Phone.ToLower(), $"%{asciiDigits}%"))) ||
                (b.AadharNumber != null && (
                    EF.Functions.Like(b.AadharNumber.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(b.AadharNumber.ToLower(), $"%{asciiDigits}%"))));
        }

        if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All")
        {
            if (Enum.TryParse<BorrowerStatus>(statusFilter, out var status))
            {
                query = query.Where(b => b.Status == status);
            }
        }

        if (entryDateFrom.HasValue)
        {
            query = query.Where(b => b.EntryDate >= entryDateFrom.Value);
        }

        if (entryDateTo.HasValue)
        {
            query = query.Where(b => b.EntryDate <= entryDateTo.Value);
        }

        var borrowers = await query
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return borrowers.Select(b => new BorrowerSearchResult(
            b.Id,
            b.BorrowerNumber,
            translationService != null ? translationService.Translate(b.Name, currentLang) : b.Name,
            b.Contact,
            b.Status.ToString(),
            b.Loans?.Sum(l => l.Principal.Amount) ?? 0,
            b.EntryDate,
            (DateTime?)null)).ToList();
    }

    public async Task<IReadOnlyList<TransactionSearchResult>> SearchTransactionsAsync(
        string? searchTerm,
        string? typeFilter,
        DateTime? fromDate,
        DateTime? toDate,
        decimal? minAmount,
        decimal? maxAmount,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDhar.Infrastructure.Persistence.DhirDharDbContext>();
        var localizationService = scope.ServiceProvider.GetService<DhirDhar.Application.Localization.ILocalizationService>();
        var translationService = scope.ServiceProvider.GetService<ITranslationService>();
        var currentLang = localizationService?.CurrentLanguage ?? "gu-IN";

        var query = dbContext.Transactions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var rawTerm = searchTerm.Trim();
            if (rawTerm.StartsWith("DHIRDHAR|ACCOUNT|", StringComparison.OrdinalIgnoreCase))
            {
                rawTerm = rawTerm.Substring("DHIRDHAR|ACCOUNT|".Length).Trim();
            }
            else if (rawTerm.StartsWith("DHIRDHAR|", StringComparison.OrdinalIgnoreCase))
            {
                var parts = rawTerm.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 3) rawTerm = parts[2];
            }
            rawTerm = rawTerm.TrimStart('#').Trim();
            var term = rawTerm.ToLowerInvariant();
            var englishTerm = ScriptTranslator.ToEnglish(rawTerm).Trim().ToLowerInvariant();
            var gujaratiTerm = ScriptTranslator.ToGujarati(rawTerm).Trim();
            var hindiTerm = ScriptTranslator.ToHindi(rawTerm).Trim();
            var asciiDigits = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(rawTerm);

            query = query.Where(t =>
                (t.Reference != null && (
                    EF.Functions.Like(t.Reference, $"%{rawTerm}%") ||
                    EF.Functions.Like(t.Reference.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(t.Reference.ToLower(), $"%{englishTerm}%") ||
                    EF.Functions.Like(t.Reference.ToLower(), $"%{asciiDigits}%"))) ||
                (t.Description != null && (
                    EF.Functions.Like(t.Description, $"%{rawTerm}%") ||
                    EF.Functions.Like(t.Description.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(t.Description.ToLower(), $"%{englishTerm}%") ||
                    EF.Functions.Like(t.Description, $"%{gujaratiTerm}%") ||
                    EF.Functions.Like(t.Description, $"%{hindiTerm}%"))) ||
                (t.Borrower != null && (
                    EF.Functions.Like(t.Borrower.Name, $"%{rawTerm}%") ||
                    EF.Functions.Like(t.Borrower.Name.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(t.Borrower.Name.ToLower(), $"%{englishTerm}%") ||
                    EF.Functions.Like(t.Borrower.Name, $"%{gujaratiTerm}%") ||
                    EF.Functions.Like(t.Borrower.Name, $"%{hindiTerm}%") ||
                    EF.Functions.Like(t.Borrower.BorrowerNumber.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(t.Borrower.BorrowerNumber.ToLower(), $"%{englishTerm}%") ||
                    EF.Functions.Like(t.Borrower.BorrowerNumber.ToLower(), $"%{asciiDigits}%") ||
                    (t.Borrower.Phone != null && (
                        EF.Functions.Like(t.Borrower.Phone.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(t.Borrower.Phone.ToLower(), $"%{asciiDigits}%"))) ||
                    (t.Borrower.Village != null && (
                        EF.Functions.Like(t.Borrower.Village.ToLower(), $"%{term}%") ||
                        EF.Functions.Like(t.Borrower.Village.ToLower(), $"%{englishTerm}%") ||
                        EF.Functions.Like(t.Borrower.Village, $"%{gujaratiTerm}%") ||
                        EF.Functions.Like(t.Borrower.Village, $"%{hindiTerm}%"))))));
        }

        if (!string.IsNullOrEmpty(typeFilter) && typeFilter != "All")
        {
            if (Enum.TryParse<TransactionType>(typeFilter, out var type))
            {
                query = query.Where(t => t.Type == type);
            }
        }

        if (fromDate.HasValue)
        {
            query = query.Where(t => t.OccurredOn >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(t => t.OccurredOn <= toDate.Value);
        }

        if (minAmount.HasValue)
        {
            query = query.Where(t => t.Amount.Amount >= minAmount.Value);
        }

        if (maxAmount.HasValue)
        {
            query = query.Where(t => t.Amount.Amount <= maxAmount.Value);
        }

        var transactions = await query
            .OrderByDescending(t => t.OccurredOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var results = new List<TransactionSearchResult>();
        foreach (var txn in transactions)
        {
            var borrower = await dbContext.Borrowers
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == txn.BorrowerId, cancellationToken)
                .ConfigureAwait(false);

            var rawBName = borrower?.Name ?? "Unknown";
            var bName = borrower != null && translationService != null ? translationService.Translate(rawBName, currentLang) : rawBName;

            results.Add(new TransactionSearchResult(
                txn.Id,
                txn.TransactionDate,
                bName,
                txn.Type.ToString(),
                txn.Amount.Amount,
                txn.Reference));
        }

        return results;
    }
}

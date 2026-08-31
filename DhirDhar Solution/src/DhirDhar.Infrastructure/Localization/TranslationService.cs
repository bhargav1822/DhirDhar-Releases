using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Localization;
using DhirDhar.Domain.Entities;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Localization;

public sealed class TranslationService : ITranslationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TranslationService>? _logger;
    private readonly ILocalizationService? _localizationService;
    private readonly ConcurrentDictionary<string, string> _memoryCache = new(StringComparer.OrdinalIgnoreCase);

    public TranslationService(
        IServiceScopeFactory scopeFactory,
        ILogger<TranslationService>? logger = null,
        ILocalizationService? localizationService = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _localizationService = localizationService;
        _ = PreloadAllStoredTranslationsAsync();
    }

    private async Task PreloadAllStoredTranslationsAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var translations = await dbContext.UserTextTranslations.AsNoTracking().ToListAsync().ConfigureAwait(false);
            foreach (var t in translations)
            {
                if (!string.IsNullOrWhiteSpace(t.SourceText) && !string.IsNullOrWhiteSpace(t.TargetLanguage) && !string.IsNullOrWhiteSpace(t.TranslatedText))
                {
                    var key = GetCacheKey(t.SourceText, t.TargetLanguage);
                    _memoryCache[key] = t.TranslatedText;
                }
            }
        }
        catch
        {
            // Ignore startup preload errors; will fetch/persist on demand
        }
    }

    private static string GetCacheKey(string text, string targetLang) => $"{targetLang}:{text.Trim()}";

    public string DetectLanguage(string? text)
    {
        return ScriptTranslator.DetectLanguage(text);
    }

    public string Translate(string? text, string targetLanguageCode)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

        var targetLang = ScriptTranslator.NormalizeLanguageCode(targetLanguageCode);
        var sourceLang = DetectLanguage(text);

        // First, check if LocalizationService can localize it (dynamic interest pattern or system text)
        if (_localizationService != null)
        {
            var localized = _localizationService.LocalizeText(text, targetLang);
            if (!string.Equals(localized, text, StringComparison.Ordinal))
            {
                return localized;
            }
        }

        if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        var cacheKey = GetCacheKey(text, targetLang);
        if (_memoryCache.TryGetValue(cacheKey, out var cached))
        {
            // If cached value is legacy transliteration, discard it
            if (!cached.Contains("ઈન્ટરેસ્ટ ફોર"))
            {
                return cached;
            }
        }

        var translated = ScriptTranslator.Translate(text, targetLang);
        _memoryCache[cacheKey] = translated;

        return translated;
    }

    public async Task<string> TranslateAsync(string? text, string targetLanguageCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

        var targetLang = ScriptTranslator.NormalizeLanguageCode(targetLanguageCode);
        var sourceLang = DetectLanguage(text);

        // First, check if LocalizationService can localize it (dynamic interest pattern or system text)
        if (_localizationService != null)
        {
            var localized = _localizationService.LocalizeText(text, targetLang);
            if (!string.Equals(localized, text, StringComparison.Ordinal))
            {
                return localized;
            }
        }

        if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        var cacheKey = GetCacheKey(text, targetLang);
        if (_memoryCache.TryGetValue(cacheKey, out var cached))
        {
            if (!cached.Contains("ઈન્ટરેસ્ટ ફોર"))
            {
                return cached;
            }
        }

        var trimmedText = text.Trim();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var existing = await dbContext.UserTextTranslations
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.SourceText == trimmedText && t.TargetLanguage == targetLang, cancellationToken)
                .ConfigureAwait(false);

            if (existing != null && !string.IsNullOrWhiteSpace(existing.TranslatedText) && !existing.TranslatedText.Contains("ઈન્ટરેસ્ટ ફોર"))
            {
                _memoryCache[cacheKey] = existing.TranslatedText;
                return existing.TranslatedText;
            }

            var translated = ScriptTranslator.Translate(text, targetLang);
            _memoryCache[cacheKey] = translated;

            var entity = new UserTextTranslation(trimmedText, sourceLang, targetLang, translated);
            dbContext.UserTextTranslations.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return translated;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to query/persist translation for '{Text}' to '{TargetLang}'.", text, targetLang);
            var translated = ScriptTranslator.Translate(text, targetLang);
            _memoryCache[cacheKey] = translated;
            return translated;
        }
    }

    public async Task InvalidateTranslationsAsync(string oldText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(oldText)) return;

        var trimmed = oldText.Trim();

        // Evict from in-memory cache
        foreach (var key in _memoryCache.Keys.ToList())
        {
            if (key.EndsWith($":{trimmed}", StringComparison.OrdinalIgnoreCase))
            {
                _memoryCache.TryRemove(key, out _);
            }
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var items = await dbContext.UserTextTranslations
                .Where(t => t.SourceText == trimmed)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (items.Count > 0)
            {
                dbContext.UserTextTranslations.RemoveRange(items);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to invalidate translations for '{OldText}'.", oldText);
        }
    }

    public async Task PreloadTranslationsAsync(IEnumerable<string> texts, string targetLanguageCode, CancellationToken cancellationToken = default)
    {
        if (texts == null) return;

        var targetLang = ScriptTranslator.NormalizeLanguageCode(targetLanguageCode);
        var missingTexts = new List<string>();

        foreach (var t in texts.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var trimmed = t.Trim();
            var sourceLang = DetectLanguage(trimmed);
            if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = GetCacheKey(trimmed, targetLang);
            if (!_memoryCache.ContainsKey(key))
            {
                missingTexts.Add(trimmed);
            }
        }

        if (missingTexts.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var existingDb = await dbContext.UserTextTranslations
                .AsNoTracking()
                .Where(t => missingTexts.Contains(t.SourceText) && t.TargetLanguage == targetLang)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var existingSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in existingDb)
            {
                var key = GetCacheKey(record.SourceText, record.TargetLanguage);
                _memoryCache[key] = record.TranslatedText;
                existingSet.Add(record.SourceText);
            }

            var newEntities = new List<UserTextTranslation>();
            foreach (var missing in missingTexts)
            {
                if (!existingSet.Contains(missing))
                {
                    var sourceLang = DetectLanguage(missing);
                    var translated = ScriptTranslator.Translate(missing, targetLang);
                    var key = GetCacheKey(missing, targetLang);
                    _memoryCache[key] = translated;
                    newEntities.Add(new UserTextTranslation(missing, sourceLang, targetLang, translated));
                }
            }

            if (newEntities.Count > 0)
            {
                dbContext.UserTextTranslations.AddRange(newEntities);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to preload translations for target language '{TargetLang}'.", targetLang);
            foreach (var missing in missingTexts)
            {
                var translated = ScriptTranslator.Translate(missing, targetLang);
                _memoryCache[GetCacheKey(missing, targetLang)] = translated;
            }
        }
    }

    public async Task SetTranslationAsync(string sourceText, string targetLanguageCode, string translatedText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(translatedText)) return;

        var targetLang = ScriptTranslator.NormalizeLanguageCode(targetLanguageCode);
        var sourceLang = DetectLanguage(sourceText);
        var trimmedSource = sourceText.Trim();
        var trimmedTranslated = translatedText.Trim();

        var cacheKey = GetCacheKey(trimmedSource, targetLang);
        _memoryCache[cacheKey] = trimmedTranslated;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var existing = await dbContext.UserTextTranslations
                .FirstOrDefaultAsync(t => t.SourceText == trimmedSource && t.TargetLanguage == targetLang, cancellationToken)
                .ConfigureAwait(false);

            if (existing != null)
            {
                existing.UpdateTranslation(trimmedTranslated);
            }
            else
            {
                var entity = new UserTextTranslation(trimmedSource, sourceLang, targetLang, trimmedTranslated);
                dbContext.UserTextTranslations.Add(entity);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save translation for '{Source}' -> '{Target}'.", sourceText, targetLanguageCode);
        }
    }

    private async Task PersistTranslationAsync(string text, string sourceLang, string targetLang, string translated)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var trimmedText = text.Trim();
            var exists = await dbContext.UserTextTranslations
                .AnyAsync(t => t.SourceText == trimmedText && t.TargetLanguage == targetLang)
                .ConfigureAwait(false);

            if (!exists)
            {
                var entity = new UserTextTranslation(trimmedText, sourceLang, targetLang, translated);
                dbContext.UserTextTranslations.Add(entity);
                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Background persistence failure does not block UI rendering
        }
    }
}

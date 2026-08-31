using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DhirDhar.Application.Localization;

/// <summary>
/// Service for translating user-entered data (e.g. Borrower Name, Father Name, Village, Notes)
/// between languages with caching and persistence, ensuring source text remains untouched in the database.
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// Translates the specified user text to the target language code synchronously (using memory cache and translation engine).
    /// </summary>
    string Translate(string? text, string targetLanguageCode);

    /// <summary>
    /// Translates the specified user text to the target language code asynchronously, persisting new translations to the database.
    /// </summary>
    Task<string> TranslateAsync(string? text, string targetLanguageCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects the language script of the given text (e.g. "gu", "hi", "mr", "en", "bn", etc.).
    /// </summary>
    string DetectLanguage(string? text);

    /// <summary>
    /// Invalidates cached/persisted translations for a changed source text.
    /// </summary>
    Task InvalidateTranslationsAsync(string oldText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Preloads and caches translations for a list of texts for a target language.
    /// </summary>
    Task PreloadTranslationsAsync(IEnumerable<string> texts, string targetLanguageCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly saves and caches a language-specific translation for the specified source text.
    /// </summary>
    Task SetTranslationAsync(string sourceText, string targetLanguageCode, string translatedText, CancellationToken cancellationToken = default);
}

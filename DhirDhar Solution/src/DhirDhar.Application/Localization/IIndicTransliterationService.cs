using System;

namespace DhirDhar.Application.Localization;

/// <summary>
/// Service interface for Google Indic-style phonetic transliteration across Indic languages.
/// </summary>
public interface IIndicTransliterationService
{
    /// <summary>
    /// Transliterates text containing Latin phonetic input into target Indic script (default Gujarati),
    /// preserving whitespace, punctuation, numbers, and existing Unicode text.
    /// </summary>
    string Transliterate(string? text, string targetLanguage = "gu");

    /// <summary>
    /// Transliterates a single word/token into target Indic script.
    /// </summary>
    string TransliterateWord(string word, string targetLanguage = "gu");

    /// <summary>
    /// Returns true if the text contains Latin characters that can be transliterated into target Indic script.
    /// </summary>
    bool ShouldTransliterate(string? text, string targetLanguage);
}

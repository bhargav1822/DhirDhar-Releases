using System;

namespace DhirDhar.Application.Localization;

/// <summary>
/// Reusable service implementation for Google Indic-style phonetic transliteration.
/// </summary>
public sealed class IndicTransliterationService : IIndicTransliterationService
{
    public string Transliterate(string? text, string targetLanguage = "gu")
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

        var normalized = ScriptTranslator.NormalizeLanguageCode(targetLanguage);
        if (normalized == "en")
        {
            return text;
        }

        return ScriptTranslator.Translate(text, normalized);
    }

    public string TransliterateWord(string word, string targetLanguage = "gu")
    {
        if (string.IsNullOrWhiteSpace(word)) return word ?? string.Empty;

        var normalized = ScriptTranslator.NormalizeLanguageCode(targetLanguage);
        if (normalized == "gu")
        {
            return ScriptTranslator.ToGujarati(word);
        }
        if (normalized == "hi" || normalized == "mr")
        {
            return ScriptTranslator.ToHindi(word);
        }

        return ScriptTranslator.Translate(word, normalized);
    }

    public bool ShouldTransliterate(string? text, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var normalized = ScriptTranslator.NormalizeLanguageCode(targetLanguage);
        if (normalized == "en") return false;

        // Check if there are any Latin letters (a-z, A-Z) to transliterate
        foreach (char c in text)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
            {
                return true;
            }
        }

        return false;
    }
}

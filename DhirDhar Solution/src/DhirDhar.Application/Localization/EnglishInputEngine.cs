using System;

namespace DhirDhar.Application.Localization;

/// <summary>
/// Dedicated English / Latin input engine.
/// Passes all keystrokes and text through directly without phonetic transliteration.
/// </summary>
public sealed class EnglishInputEngine : IPhoneticLanguageEngine
{
    public static readonly EnglishInputEngine Instance = new();

    public string LanguageCode => "en-IN";
    public string LanguageName => "English";
    public bool IsPhoneticActive => false;

    public string Transliterate(string input)
    {
        return input ?? string.Empty;
    }

    public string TransliterateWord(string word)
    {
        return word ?? string.Empty;
    }
}

namespace DhirDhar.Application.Localization;

/// <summary>
/// Common contract for dedicated language phonetic transliteration engines.
/// </summary>
public interface IPhoneticLanguageEngine
{
    /// <summary>
    /// Canonical language code handled by this engine (e.g. "gu-IN", "hi-IN", "en-IN").
    /// </summary>
    string LanguageCode { get; }

    /// <summary>
    /// Friendly display name for the language engine.
    /// </summary>
    string LanguageName { get; }

    /// <summary>
    /// Whether this engine actively performs phonetic transliteration.
    /// </summary>
    bool IsPhoneticActive { get; }

    /// <summary>
    /// Transliterates arbitrary multi-token or full text string according to this engine's rules.
    /// </summary>
    string Transliterate(string input);

    /// <summary>
    /// Transliterates a single continuous word or active composition buffer.
    /// </summary>
    string TransliterateWord(string word);
}

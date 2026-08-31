using System;
using DhirDhar.Domain.Common;

namespace DhirDhar.Domain.Entities;

/// <summary>
/// Stores language-specific translations of user-entered free-text fields (such as borrower name,
/// father's name, surname, village, address, notes, transaction descriptions) separately from
/// the immutable original source text.
/// </summary>
public sealed class UserTextTranslation : Entity
{
    private UserTextTranslation()
    {
    }

    public UserTextTranslation(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        string translatedText)
        : base(Guid.NewGuid())
    {
        SourceText = sourceText?.Trim() ?? string.Empty;
        SourceLanguage = sourceLanguage?.Trim().ToLowerInvariant() ?? "en";
        TargetLanguage = targetLanguage?.Trim().ToLowerInvariant() ?? "en";
        TranslatedText = translatedText?.Trim() ?? string.Empty;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public string SourceText { get; private set; } = string.Empty;

    public string SourceLanguage { get; private set; } = string.Empty;

    public string TargetLanguage { get; private set; } = string.Empty;

    public string TranslatedText { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public void UpdateTranslation(string translatedText)
    {
        TranslatedText = translatedText?.Trim() ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
    }
}

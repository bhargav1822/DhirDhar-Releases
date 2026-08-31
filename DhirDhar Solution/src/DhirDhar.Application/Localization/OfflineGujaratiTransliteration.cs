using System;

namespace DhirDhar.Application.Localization;

/// <summary>
/// Offline Gujarati transliteration proxy calling the centralized GujaratiPhoneticEngine.
/// </summary>
public static class OfflineGujaratiTransliteration
{
    public static string Transliterate(string input)
    {
        return GujaratiPhoneticEngine.Transliterate(input);
    }
}

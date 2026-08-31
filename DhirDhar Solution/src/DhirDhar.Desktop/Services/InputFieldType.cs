namespace DhirDhar.Desktop.Services;

/// <summary>
/// Categorization of input fields to determine eligibility for phonetic transliteration.
/// Only NaturalText and SearchText fields participate in phonetic transliteration.
/// Numeric, Identifier, Date, Phone, Aadhaar, Currency, and Percentage fields bypass phonetic transliteration completely.
/// </summary>
public enum InputFieldType
{
    /// <summary>
    /// Natural-language text fields (e.g. Borrower Name, Village, Notes, Custom Ornament Type).
    /// </summary>
    NaturalText = 0,

    /// <summary>
    /// Search input fields with real-time phonetic support.
    /// </summary>
    SearchText = 1,

    /// <summary>
    /// Numeric fields (e.g. Weight, Loan Amount, Interest Rate) - always bypasses phonetic transliteration.
    /// </summary>
    Numeric = 2,

    /// <summary>
    /// Identifier / Account numbers / Codes - bypasses phonetic transliteration.
    /// </summary>
    Identifier = 3,

    /// <summary>
    /// Date fields / pickers - bypasses phonetic transliteration.
    /// </summary>
    Date = 4,

    /// <summary>
    /// Phone / Mobile number fields - bypasses phonetic transliteration.
    /// </summary>
    Phone = 5,

    /// <summary>
    /// Aadhaar number fields - bypasses phonetic transliteration.
    /// </summary>
    Aadhaar = 6,

    /// <summary>
    /// Currency / financial value fields - bypasses phonetic transliteration.
    /// </summary>
    Currency = 7,

    /// <summary>
    /// Percentage fields - bypasses phonetic transliteration.
    /// </summary>
    Percentage = 8
}

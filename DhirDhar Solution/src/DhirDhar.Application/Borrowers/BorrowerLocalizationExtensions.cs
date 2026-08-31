using System;
using System.Collections.Generic;
using System.Linq;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Localization;

namespace DhirDhar.Application.Borrowers;

public static class BorrowerLocalizationExtensions
{
    public static BorrowerSummary Localize(this BorrowerSummary summary, ITranslationService? translationService, string targetLanguageCode)
    {
        if (summary is null) return summary!;
        if (translationService is null || string.IsNullOrWhiteSpace(targetLanguageCode)) return summary;

        var normLang = ScriptTranslator.NormalizeLanguageCode(targetLanguageCode);

        var localizedName = !string.IsNullOrWhiteSpace(summary.Name)
            ? translationService.Translate(summary.Name, normLang)
            : summary.Name;

        var localizedFather = !string.IsNullOrWhiteSpace(summary.FatherName)
            ? translationService.Translate(summary.FatherName, normLang)
            : summary.FatherName;

        var localizedSurname = !string.IsNullOrWhiteSpace(summary.Surname)
            ? translationService.Translate(summary.Surname, normLang)
            : summary.Surname;

        var localizedVillage = !string.IsNullOrWhiteSpace(summary.Village)
            ? translationService.Translate(summary.Village, normLang)
            : summary.Village;

        var localizedLoanType = !string.IsNullOrWhiteSpace(summary.LoanType)
            ? translationService.Translate(summary.LoanType, normLang)
            : summary.LoanType;

        var localizedOrnamentType = !string.IsNullOrWhiteSpace(summary.OrnamentType)
            ? translationService.Translate(summary.OrnamentType, normLang)
            : summary.OrnamentType;

        var localizedBorrowerNumber = !string.IsNullOrWhiteSpace(summary.BorrowerNumber)
            ? ScriptTranslator.ConvertDigitsToIndic(summary.BorrowerNumber, normLang)
            : summary.BorrowerNumber;

        var localizedContact = !string.IsNullOrWhiteSpace(summary.Contact)
            ? ScriptTranslator.ConvertDigitsToIndic(summary.Contact, normLang)
            : summary.Contact;

        var localizedAadhar = !string.IsNullOrWhiteSpace(summary.AadharNumber)
            ? ScriptTranslator.ConvertDigitsToIndic(summary.AadharNumber, normLang)
            : summary.AadharNumber;

        return new BorrowerSummary(
            summary.Id,
            localizedBorrowerNumber,
            localizedName,
            localizedContact,
            summary.Status,
            summary.EntryDate,
            summary.TotalDeposits,
            summary.TotalWithdrawals,
            summary.OutstandingAmount,
            summary.LastTransactionDate,
            localizedFather,
            localizedSurname,
            localizedVillage,
            localizedAadhar,
            summary.BorrowerPhotoPath,
            summary.OrnamentPhotoPath,
            localizedLoanType,
            localizedOrnamentType,
            summary.OrnamentWeight,
            summary.LoanAmount,
            summary.LoanDate,
            summary.InterestRate,
            summary.ClosedDate);
    }

    public static IEnumerable<BorrowerSummary> Localize(this IEnumerable<BorrowerSummary> summaries, ITranslationService? translationService, string targetLanguageCode)
    {
        if (summaries is null) return Enumerable.Empty<BorrowerSummary>();
        if (translationService is null || string.IsNullOrWhiteSpace(targetLanguageCode)) return summaries;

        return summaries.Select(s => s.Localize(translationService, targetLanguageCode));
    }
}

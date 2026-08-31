using System;
using System.Collections.Generic;
using System.Linq;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Localization;
using Xunit;

namespace DhirDhar.Application.Tests;

public class BorrowerLocalizationExtensionTests
{
    private sealed class StubTranslationService : ITranslationService
    {
        public string Translate(string? text, string targetLanguageCode)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
            return ScriptTranslator.Translate(text, targetLanguageCode);
        }

        public Task<string> TranslateAsync(string? text, string targetLanguageCode, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Translate(text, targetLanguageCode));
        }

        public string DetectLanguage(string? text)
        {
            return ScriptTranslator.DetectLanguage(text);
        }

        public Task InvalidateTranslationsAsync(string oldText, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PreloadTranslationsAsync(IEnumerable<string> texts, string targetLanguageCode, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SetTranslationAsync(string sourceText, string targetLanguageCode, string translatedText, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Localize_EnglishBorrower_TranslatesFieldsToGujarati_PreservingIdentifiers()
    {
        var translationService = new StubTranslationService();
        var original = new BorrowerSummary(
            Id: Guid.NewGuid(),
            BorrowerNumber: "DJ102",
            Name: "Ramsinh Valsinh Katara",
            Contact: "9876543210",
            Status: "Active",
            EntryDate: new DateTime(2026, 1, 15),
            TotalDeposits: 5000m,
            TotalWithdrawals: 15000m,
            OutstandingAmount: 10000m,
            LastTransactionDate: new DateTime(2026, 2, 1),
            FatherName: "Valsinh",
            Surname: "Katara",
            Village: "Sukhsar",
            AadharNumber: "123456789012",
            BorrowerPhotoPath: "C:\\photos\\b1.jpg",
            OrnamentPhotoPath: "C:\\photos\\o1.jpg",
            LoanType: "Gold",
            OrnamentType: "Ring",
            OrnamentWeight: 12.5m,
            LoanAmount: 15000m,
            LoanDate: new DateTime(2026, 1, 15),
            InterestRate: 3.00m,
            ClosedDate: null);

        var localized = original.Localize(translationService, "gu-IN");

        // Translated text fields
        Assert.Equal("રામસિંહ વાલસિંહ કટારા", localized.Name);
        Assert.Equal("વાલસિંહ", localized.FatherName);
        Assert.Equal("કટારા", localized.Surname);
        Assert.Equal("સુખસર", localized.Village);
        Assert.Equal("સોનું", localized.LoanType);
        Assert.Equal("વીંટી", localized.OrnamentType);

        // Localized identifiers and numbers for Gujarati
        Assert.Equal("DJ૧૦૨", localized.BorrowerNumber);
        Assert.Equal("૯૮૭૬૫૪૩૨૧૦", localized.Contact);
        Assert.Equal("૧૨૩૪૫૬૭૮૯૦૧૨", localized.AadharNumber);
        Assert.Equal(original.Id, localized.Id);
        Assert.Equal("Active", localized.Status);
        Assert.Equal(5000m, localized.TotalDeposits);
        Assert.Equal(15000m, localized.TotalWithdrawals);
        Assert.Equal(10000m, localized.OutstandingAmount);
        Assert.Equal(12.5m, localized.OrnamentWeight);
        Assert.Equal(15000m, localized.LoanAmount);
        Assert.Equal(3.00m, localized.InterestRate);
        Assert.Equal(original.EntryDate, localized.EntryDate);
        Assert.Equal(original.LoanDate, localized.LoanDate);
        Assert.Equal(original.LastTransactionDate, localized.LastTransactionDate);
        Assert.Equal(original.BorrowerPhotoPath, localized.BorrowerPhotoPath);
        Assert.Equal(original.OrnamentPhotoPath, localized.OrnamentPhotoPath);
    }

    [Fact]
    public void Localize_EnglishBorrower_TranslatesFieldsToHindi()
    {
        var translationService = new StubTranslationService();
        var original = new BorrowerSummary(
            Id: Guid.NewGuid(),
            BorrowerNumber: "DJ102",
            Name: "Ramsinh Valsinh Katara",
            Contact: "9876543210",
            Status: "Active",
            EntryDate: new DateTime(2026, 1, 15),
            TotalDeposits: 0m,
            TotalWithdrawals: 10000m,
            OutstandingAmount: 10000m,
            LastTransactionDate: null,
            FatherName: "Valsinh",
            Surname: "Katara",
            Village: "Sukhsar",
            AadharNumber: null,
            BorrowerPhotoPath: null,
            OrnamentPhotoPath: null,
            LoanType: "Silver",
            OrnamentType: "Necklace",
            OrnamentWeight: 50m,
            LoanAmount: 10000m,
            LoanDate: new DateTime(2026, 1, 15),
            InterestRate: 3.00m,
            ClosedDate: null);

        var localized = original.Localize(translationService, "hi-IN");

        Assert.Equal("रामसिंह वालसिंह कटारा", localized.Name);
        Assert.Equal("वालसिंह", localized.FatherName);
        Assert.Equal("कटारा", localized.Surname);
        Assert.Equal("सुखसर", localized.Village);
        Assert.Equal("चांदी", localized.LoanType);
        Assert.Equal("हार", localized.OrnamentType);
        Assert.Equal("DJ१०२", localized.BorrowerNumber);
    }

    [Fact]
    public void Localize_GujaratiBorrower_TranslatesFieldsToEnglish()
    {
        var translationService = new StubTranslationService();
        var original = new BorrowerSummary(
            Id: Guid.NewGuid(),
            BorrowerNumber: "B-205",
            Name: "રામસિંહ વાલસિંહ કટારા",
            Contact: "9876543210",
            Status: "Active",
            EntryDate: new DateTime(2026, 1, 15),
            TotalDeposits: 0m,
            TotalWithdrawals: 10000m,
            OutstandingAmount: 10000m,
            LastTransactionDate: null,
            FatherName: "વાલસિંહ",
            Surname: "કટારા",
            Village: "સુખસર",
            AadharNumber: null,
            BorrowerPhotoPath: null,
            OrnamentPhotoPath: null,
            LoanType: "સોનું",
            OrnamentType: "વીંટી",
            OrnamentWeight: 10m,
            LoanAmount: 10000m,
            LoanDate: null,
            InterestRate: 3.00m,
            ClosedDate: null);

        var localized = original.Localize(translationService, "en-US");

        Assert.Equal("Ramsinh Valsinh Katara", localized.Name);
        Assert.Equal("Valsinh", localized.FatherName);
        Assert.Equal("Katara", localized.Surname);
        Assert.Equal("Sukhsar", localized.Village);
        Assert.Equal("Gold", localized.LoanType);
        Assert.Equal("Ring", localized.OrnamentType);
        Assert.Equal("B-205", localized.BorrowerNumber);
    }

    [Fact]
    public void Localize_Collection_LocalizesAllItemsIndividually()
    {
        var translationService = new StubTranslationService();
        var list = new List<BorrowerSummary>
        {
            new(Guid.NewGuid(), "DJ01", "Ramsinh Valsinh Katara", null, "Active", DateTime.Today, 0m, 0m, 0m, null, "Valsinh", "Katara", "Sukhsar", null, null, null, "Gold", "Ring", null, null, null, null, null),
            new(Guid.NewGuid(), "DJ02", "Bhargav Pravinchandra Panchal", null, "Active", DateTime.Today, 0m, 0m, 0m, null, "Pravinchandra", "Panchal", "Patan", null, null, null, "Silver", "Bangles", null, null, null, null, null)
        };

        var localized = list.Localize(translationService, "gu-IN").ToList();

        Assert.Equal(2, localized.Count);
        Assert.Equal("રામસિંહ વાલસિંહ કટારા", localized[0].Name);
        Assert.Equal("સુખસર", localized[0].Village);
        Assert.Equal("સોનું", localized[0].LoanType);
        Assert.Equal("વીંટી", localized[0].OrnamentType);
        Assert.Equal("DJ૦૧", localized[0].BorrowerNumber);

        Assert.Equal("ભાર્ગવ પ્રવિણચંદ્ર પંચાલ", localized[1].Name);
        Assert.Equal("પાટણ", localized[1].Village);
        Assert.Equal("ચાંદી", localized[1].LoanType);
        Assert.Equal("બંગડીઓ", localized[1].OrnamentType);
        Assert.Equal("DJ૦૨", localized[1].BorrowerNumber);
    }
}

using System;
using DhirDhar.Application.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DhirDhar.Desktop.Services;

public interface IAppTypographyService
{
    FontFamily GetFontForLanguage(string? languageCode);
    FontFamily CurrentFontFamily { get; }
    void ApplyCurrentLanguageFont();
}

public sealed class AppTypographyService : IAppTypographyService, IDisposable
{
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<AppTypographyService> _logger;
    private bool _disposed;

    // Verified Unicode fonts embedded in the package
    private const string NotoGujarati = "Noto Sans Gujarati, ms-appx:///Assets/Fonts/NotoSansGujarati.ttf#Noto Sans Gujarati";
    private const string NotoGujaratiAlt = "ms-appx:///Assets/NotoSansGujarati.ttf#Noto Sans Gujarati";
    private const string NotoDevanagari = "Noto Sans Devanagari, ms-appx:///Assets/Fonts/NotoSansDevanagari.ttf#Noto Sans Devanagari";
    private const string EnglishFallback = "Segoe UI, Nirmala UI";

    public AppTypographyService(ILocalizationService localizationService, ILogger<AppTypographyService> logger)
    {
        _localizationService = localizationService;
        _logger = logger;
        _localizationService.LanguageChanged += OnLanguageChanged;
        ApplyCurrentLanguageFont();
    }

    public FontFamily CurrentFontFamily
    {
        get
        {
            // Must be called on UI thread; if not, fallback to string-based creation via dispatcher
            return new FontFamily(GetFontSourceForLanguage(_localizationService.CurrentLanguage));
        }
    }

    public FontFamily GetFontForLanguage(string? languageCode)
    {
        return new FontFamily(GetFontSourceForLanguage(languageCode));
    }

    private string GetFontSourceForLanguage(string? languageCode)
    {
        var norm = ScriptTranslator.NormalizeLanguageCode(languageCode ?? _localizationService.CurrentLanguage);
        var full = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeLanguageCode(languageCode ?? _localizationService.CurrentLanguage);
        if (full == "gu-IN" || norm == "gu")
        {
            return $"{NotoGujarati}, {NotoDevanagari}, {EnglishFallback}";
        }
        if (full == "hi-IN" || norm == "hi" || norm == "mr")
        {
            return $"{NotoDevanagari}, {NotoGujarati}, {EnglishFallback}";
        }
        return $"{EnglishFallback}, {NotoGujarati}, {NotoDevanagari}";
    }

    public void ApplyCurrentLanguageFont()
    {
        var lang = _localizationService.CurrentLanguage;
        var fontSource = GetFontSourceForLanguage(lang);

        void ApplyOnUI()
        {
            try
            {
                var font = new FontFamily(fontSource);
                _logger.LogInformation("[TYPOGRAPHY] Apply font for {Language}: {Font}", lang, font.Source);
                var app = Microsoft.UI.Xaml.Application.Current;
                if (app != null)
                {
                    app.Resources["AppFontFamily"] = font;
                }
                if (App.MainWindow?.Content is Microsoft.UI.Xaml.Controls.Control ctrl)
                {
                    ctrl.FontFamily = font;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TYPOGRAPHY] Failed to apply font for {Language}", lang);
            }
        }

        var queue = App.MainDispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (queue != null && !queue.HasThreadAccess)
        {
            queue.TryEnqueue(() => ApplyOnUI());
        }
        else
        {
            ApplyOnUI();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyCurrentLanguageFont();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _localizationService.LanguageChanged -= OnLanguageChanged;
    }
}

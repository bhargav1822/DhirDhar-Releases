using System;
using System.Runtime.CompilerServices;
using DhirDhar.Application.Localization;
using DhirDhar.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Helpers;

/// <summary>
/// Attached behavior for WinUI 3 DatePicker, CalendarDatePicker, and CalendarView controls
/// ensuring automatic localization matching the active DhirDhar application language.
/// </summary>
public static class DatePickerLocalization
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DatePickerLocalization),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly ConditionalWeakTable<FrameworkElement, LocalizationWatcher> Watchers = new();

    public static bool GetIsEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        if ((bool)e.NewValue)
        {
            Attach(element);
        }
        else
        {
            Detach(element);
        }
    }

    public static void Attach(FrameworkElement element)
    {
        if (Watchers.TryGetValue(element, out _))
        {
            return;
        }

        var watcher = new LocalizationWatcher(element);
        Watchers.Add(element, watcher);
        watcher.Apply();
    }

    public static void Detach(FrameworkElement element)
    {
        if (Watchers.TryGetValue(element, out var watcher))
        {
            watcher.Dispose();
            Watchers.Remove(element);
        }
    }

    private sealed class LocalizationWatcher : IDisposable
    {
        private readonly WeakReference<FrameworkElement> _elementRef;
        private ILocalizationService? _localizationService;
        private bool _isDisposed;

        public LocalizationWatcher(FrameworkElement element)
        {
            _elementRef = new WeakReference<FrameworkElement>(element);
            element.Loaded += OnElementLoaded;
            element.Unloaded += OnElementUnloaded;
        }

        private void OnElementLoaded(object sender, RoutedEventArgs e)
        {
            SubscribeToLocalization();
            Apply();
        }

        private void OnElementUnloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeFromLocalization();
        }

        private void SubscribeToLocalization()
        {
            UnsubscribeFromLocalization();
            _localizationService = App.ServiceProvider?.GetService<ILocalizationService>();
            if (_localizationService != null)
            {
                _localizationService.LanguageChanged += OnLanguageChanged;
            }
        }

        private void UnsubscribeFromLocalization()
        {
            if (_localizationService != null)
            {
                _localizationService.LanguageChanged -= OnLanguageChanged;
                _localizationService = null;
            }
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            if (_elementRef.TryGetTarget(out var element))
            {
                if (element.DispatcherQueue is { } queue)
                {
                    queue.TryEnqueue(Apply);
                }
                else
                {
                    Apply();
                }
            }
        }

        public void Apply()
        {
            if (_isDisposed) return;
            if (!_elementRef.TryGetTarget(out var element)) return;

            var locService = _localizationService ?? App.ServiceProvider?.GetService<ILocalizationService>();
            var currentLang = locService?.CurrentLanguage ?? "en-US";
            var normalizedTag = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeLanguageCode(currentLang);

            try
            {
                element.Language = normalizedTag;

                if (element is DatePicker datePicker)
                {
                    datePicker.Language = normalizedTag;
                    datePicker.CalendarIdentifier = Windows.Globalization.CalendarIdentifiers.Gregorian;
                }
                else if (element is CalendarDatePicker calendarDatePicker)
                {
                    calendarDatePicker.Language = normalizedTag;
                    calendarDatePicker.CalendarIdentifier = Windows.Globalization.CalendarIdentifiers.Gregorian;
                    calendarDatePicker.FirstDayOfWeek = Windows.Globalization.DayOfWeek.Sunday;
                }
                else if (element is CalendarView calendarView)
                {
                    calendarView.Language = normalizedTag;
                    calendarView.CalendarIdentifier = Windows.Globalization.CalendarIdentifiers.Gregorian;
                    calendarView.FirstDayOfWeek = Windows.Globalization.DayOfWeek.Sunday;
                }
            }
            catch
            {
                // Fallback gracefully without interrupting UI rendering
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            UnsubscribeFromLocalization();

            if (_elementRef.TryGetTarget(out var element))
            {
                element.Loaded -= OnElementLoaded;
                element.Unloaded -= OnElementUnloaded;
            }
        }
    }
}

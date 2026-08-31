using System.ComponentModel;
using System.Runtime.CompilerServices;
using DhirDhar.Application.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace DhirDhar.Desktop.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    private ILocalizationService? _localizationService;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected ILocalizationService? LocalizationService => _localizationService;

    protected void AttachLocalization(ILocalizationService localizationService)
    {
        if (ReferenceEquals(_localizationService, localizationService))
        {
            return;
        }

        if (_localizationService != null)
        {
            _localizationService.LanguageChanged -= OnLanguageChanged;
        }

        _localizationService = localizationService;
        if (_localizationService != null)
        {
            _localizationService.LanguageChanged += OnLanguageChanged;
        }
    }

    protected string L(string key) => ResolveLocalization?.GetString(key) ?? key;
    protected string GetString(string key) => L(key);

    protected string LocalizeText(string? text) => ResolveLocalization?.LocalizeText(text) ?? text ?? string.Empty;

    private ILocalizationService? ResolveLocalization => _localizationService ?? App.ServiceProvider?.GetService<ILocalizationService>();

    protected string LocalizeDigits(string? value) => ResolveLocalization?.LocalizeDigits(value) ?? value ?? string.Empty;

    protected string LCur(decimal amount) => ResolveLocalization?.ToLocalizedCurrency(amount) ?? amount.ToString("N2");

    protected string LCurNegative(decimal amount) => ResolveLocalization?.ToLocalizedCurrency(amount, true) ?? $"-₹ {Math.Abs(amount):N2}";

    protected string LNum(decimal amount, string format = "N2") => ResolveLocalization?.ToLocalizedDecimal(amount, format) ?? amount.ToString(format);

    protected string LNum(long value) => ResolveLocalization?.ToLocalizedInteger(value) ?? value.ToString("N0");

    protected string LDate(DateTime value, string format = "dd-MM-yyyy") => ResolveLocalization?.ToLocalizedDate(value, format) ?? value.ToString(format);

    protected string LDate(DateTime? value, string format = "dd-MM-yyyy") => value.HasValue ? LDate(value.Value, format) : string.Empty;

    protected string LDateTime(DateTime value, string format = "g") => ResolveLocalization?.ToLocalizedDateTime(value, format) ?? value.ToString(format);

    protected string LTime(DateTime value, string format = "hh:mm:ss tt") => ResolveLocalization?.ToLocalizedTime(value, format) ?? value.ToString(format);

    protected string LPct(decimal value, string format = "N2") => ResolveLocalization?.ToLocalizedPercentage(value, format) ?? $"{value:N2}%";

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(string.Empty);
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher = App.MainDispatcherQueue;
        if (dispatcher == null)
        {
            try
            {
                dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            }
            catch
            {
                // Fallback when running outside WinUI thread context (e.g. background worker or test runner)
            }
        }

        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected void RunOnUiThread(Action action)
    {
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher = App.MainDispatcherQueue;
        if (dispatcher == null)
        {
            try
            {
                dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            }
            catch
            {
            }
        }

        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(() => action());
            return;
        }

        action();
    }
}
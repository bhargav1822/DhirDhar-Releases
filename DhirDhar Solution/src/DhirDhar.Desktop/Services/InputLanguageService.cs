using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Services;

/// <summary>
/// Backward-compatible adapter for IInputLanguageService forwarding to DhirDharInputEngine.
/// </summary>
public sealed class InputLanguageService : IInputLanguageService
{
    private readonly IDhirDharInputEngine _engine;

    public InputLanguageService(IDhirDharInputEngine engine)
    {
        _engine = engine;
    }

    public Guid InstanceId => _engine.InstanceId;

    public InputServiceState State => ((IInputLanguageService)_engine).State;

    public InputLanguageInfo Current => ((IInputLanguageService)_engine).Current;

    public string TargetLanguage => ((IInputLanguageService)_engine).TargetLanguage;

    public bool IsIndicActive => ((IInputLanguageService)_engine).IsIndicActive;

    public IndicInputMode CurrentMode
    {
        get => ((IInputLanguageService)_engine).CurrentMode;
        set => ((IInputLanguageService)_engine).CurrentMode = value;
    }

    public FrameworkElement? CurrentTarget => ((IInputLanguageService)_engine).CurrentTarget;

    public event EventHandler<InputLanguageChangedEventArgs>? InputLanguageChanged
    {
        add => ((IInputLanguageService)_engine).InputLanguageChanged += value;
        remove => ((IInputLanguageService)_engine).InputLanguageChanged -= value;
    }

    public event EventHandler<IndicInputMode>? InputModeChanged
    {
        add => ((IInputLanguageService)_engine).InputModeChanged += value;
        remove => ((IInputLanguageService)_engine).InputModeChanged -= value;
    }

    public event EventHandler<FrameworkElement?>? TargetChanged
    {
        add => ((IInputLanguageService)_engine).TargetChanged += value;
        remove => ((IInputLanguageService)_engine).TargetChanged -= value;
    }

    public void InitializeOnce() => ((IInputLanguageService)_engine).InitializeOnce();

    public void SetLanguage(string languageCode) => _engine.SetLanguage(languageCode);

    public void SetTarget(FrameworkElement? control) => ((IInputLanguageService)_engine).SetTarget(control);

    public void RegisterTextBox(TextBox textBox) => _engine.RegisterTextField(textBox, InputFieldType.NaturalText);

    public void UnregisterTextBox(TextBox textBox) => _engine.UnregisterTextField(textBox);

    public string Transliterate(string input) => _engine.Transliterate(input);

    public string TransliterateWord(string word) => _engine.TransliterateWord(word);

    public void RegisterWindow(IntPtr hwnd) => ((IInputLanguageService)_engine).RegisterWindow(hwnd);

    public void Refresh() => ((IInputLanguageService)_engine).Refresh();

    public void Dispose() => _engine.Dispose();
}
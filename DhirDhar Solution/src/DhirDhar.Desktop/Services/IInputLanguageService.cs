using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Services;

public enum IndicInputMode
{
    IndicPhonetic = 0,    // Phonetic Transliteration (e.g., "bhargav" -> "ભાર્ગવ")
    NativeIme = 1,        // Native Direct Unicode Input
    EnglishLatin = 2      // English / Latin Direct Input
}

public enum InputServiceState
{
    Uninitialized = 0,
    Initializing = 1,
    Ready = 2,
    Failed = 3,
    Disposed = 4
}

public sealed record InputLanguageInfo(
    string LanguageCode,
    string LanguageDisplayName,
    IntPtr ActiveHkl,
    bool IsAvailable);

public sealed class InputLanguageChangedEventArgs(InputLanguageInfo info) : EventArgs
{
    public InputLanguageInfo Info { get; } = info;
}

public interface IInputLanguageService : IDisposable
{
    Guid InstanceId { get; }

    InputServiceState State { get; }

    InputLanguageInfo Current { get; }

    string TargetLanguage { get; }

    bool IsIndicActive { get; }

    IndicInputMode CurrentMode { get; set; }

    FrameworkElement? CurrentTarget { get; }

    event EventHandler<InputLanguageChangedEventArgs>? InputLanguageChanged;

    event EventHandler<IndicInputMode>? InputModeChanged;

    event EventHandler<FrameworkElement?>? TargetChanged;

    void InitializeOnce();

    void SetLanguage(string languageCode);

    void SetTarget(FrameworkElement? control);

    void RegisterTextBox(TextBox textBox);

    void UnregisterTextBox(TextBox textBox);

    string Transliterate(string input);

    string TransliterateWord(string word);

    void RegisterWindow(IntPtr hwnd);

    void Refresh();
}
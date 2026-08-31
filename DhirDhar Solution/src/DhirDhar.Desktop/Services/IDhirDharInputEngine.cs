using System;
using DhirDhar.Application.Localization;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Services;

/// <summary>
/// Active state of the composition buffer.
/// </summary>
public enum InputCompositionState
{
    Inactive = 0,
    Composing = 1,
    Committed = 2
}

/// <summary>
/// Authoritative, application-local dedicated input engine interface for DhirDhar.
/// Controls language selection, incremental composition, and field registration.
/// </summary>
public interface IDhirDharInputEngine : IDisposable
{
    Guid InstanceId { get; }

    string ActiveLanguage { get; }

    string CurrentLanguage => ActiveLanguage;

    TextBox? ActiveTextBox { get; }

    string InputBuffer { get; }

    string CurrentWordBuffer { get; }

    InputCompositionState CompositionState { get; }

    int CaretPosition { get; }

    int SelectionStart { get; }

    int SelectionLength { get; }

    bool IsProcessingInput { get; }

    bool IsComposing { get; }

    InputFieldType? ActiveFieldType { get; }

    IPhoneticLanguageEngine ActiveLanguageEngine { get; }

    event EventHandler<string>? LanguageChanged;

    event EventHandler<TextBox?>? ActiveTextBoxChanged;

    void SetLanguage(string languageCode);

    void RegisterTextField(TextBox textBox, InputFieldType type = InputFieldType.NaturalText);

    void UnregisterTextField(TextBox textBox);

    void SetTarget(TextBox? textBox);

    string Transliterate(string input);

    string TransliterateWord(string word);

    void CommitComposition(TextBox? textBox = null);

    void CommitActiveComposition(TextBox? textBox = null);

    void HandlePreviewKeyDown(TextBox textBox, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e);

    void HandleCharacterReceived(TextBox textBox, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs e);

    void HandleSelectionChanged(TextBox textBox);

    void ResetState();
}

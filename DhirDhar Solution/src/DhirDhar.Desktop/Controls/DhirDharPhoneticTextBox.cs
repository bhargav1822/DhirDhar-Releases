using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using DhirDhar.Desktop.Helpers;
using DhirDhar.Desktop.Services;
using Windows.System;

namespace DhirDhar.Desktop.Controls;

/// <summary>
/// Dedicated application-local phonetic text box control for DhirDhar.
/// Directly intercepts keyboard input, manages stateful composition with DhirDharInputEngine,
/// applies verified Unicode output with programmatic re-entrancy protection, and updates
/// language typography automatically.
/// </summary>
public class DhirDharPhoneticTextBox : TextBox
{
    public static readonly DependencyProperty FieldTypeProperty =
        DependencyProperty.Register(
            nameof(FieldType),
            typeof(InputFieldType),
            typeof(DhirDharPhoneticTextBox),
            new PropertyMetadata(InputFieldType.NaturalText, OnFieldTypeChanged));

    public static readonly DependencyProperty IsPhoneticEnabledProperty =
        DependencyProperty.Register(
            nameof(IsPhoneticEnabled),
            typeof(bool),
            typeof(DhirDharPhoneticTextBox),
            new PropertyMetadata(true, OnIsPhoneticEnabledChanged));

    public static readonly DependencyProperty IsTransliterationDisabledProperty =
        DependencyProperty.Register(
            nameof(IsTransliterationDisabled),
            typeof(bool),
            typeof(DhirDharPhoneticTextBox),
            new PropertyMetadata(false, OnIsTransliterationDisabledChanged));

    private bool _isApplyingInput;
    private IDhirDharInputEngine? _inputEngine;

    public DhirDharPhoneticTextBox()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        GotFocus += OnGotFocus;
        LostFocus += OnLostFocus;
        PreviewKeyDown += OnPreviewKeyDown;
        CharacterReceived += OnCharacterReceived;
        SelectionChanged += OnSelectionChanged;
    }

    public InputFieldType FieldType
    {
        get => (InputFieldType)GetValue(FieldTypeProperty);
        set => SetValue(FieldTypeProperty, value);
    }

    public bool IsPhoneticEnabled
    {
        get => (bool)GetValue(IsPhoneticEnabledProperty);
        set => SetValue(IsPhoneticEnabledProperty, value);
    }

    public bool IsTransliterationDisabled
    {
        get => (bool)GetValue(IsTransliterationDisabledProperty);
        set => SetValue(IsTransliterationDisabledProperty, value);
    }

    /// <summary>
    /// Guard flag indicating that the control is writing programmatic Unicode text output.
    /// </summary>
    public bool IsApplyingInput
    {
        get => _isApplyingInput;
        internal set => _isApplyingInput = value;
    }

    private IDhirDharInputEngine? InputEngine =>
        _inputEngine ??= (App.ServiceProvider?.GetService<IDhirDharInputEngine>() ??
                          App.ServiceProvider?.GetService<IInputLanguageService>() as IDhirDharInputEngine);

    private static void OnFieldTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DhirDharPhoneticTextBox control)
        {
            control.SyncRegistration();
        }
    }

    private static void OnIsPhoneticEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DhirDharPhoneticTextBox control)
        {
            control.SyncRegistration();
        }
    }

    private static void OnIsTransliterationDisabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DhirDharPhoneticTextBox control)
        {
            control.SyncRegistration();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SyncRegistration();
        UpdateTypography();

        if (InputEngine != null)
        {
            InputEngine.LanguageChanged -= OnLanguageChanged;
            InputEngine.LanguageChanged += OnLanguageChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (InputEngine != null)
        {
            InputEngine.LanguageChanged -= OnLanguageChanged;
        }
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        SyncRegistration();
        UpdateTypography();
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        InputEngine?.CommitActiveComposition(this);
    }

    private void OnLanguageChanged(object? sender, string langCode)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            UpdateTypography();
        });
    }

    private void SyncRegistration()
    {
        if (IsTransliterationDisabled || !IsPhoneticEnabled)
        {
            InputEngine?.UnregisterTextField(this);
        }
        else
        {
            InputEngine?.RegisterTextField(this, FieldType);
        }
    }

    private void UpdateTypography()
    {
        try
        {
            var langCode = InputEngine?.CurrentLanguage ?? "gu-IN";
            if (string.Equals(langCode, "gu-IN", StringComparison.OrdinalIgnoreCase))
            {
                if (Microsoft.UI.Xaml.Application.Current?.Resources.TryGetValue("GujaratiFontFamily", out var fontRes) == true && fontRes is FontFamily ff)
                {
                    FontFamily = ff;
                }
                else
                {
                    FontFamily = new FontFamily("ms-appx:///Assets/Fonts/NotoSansGujarati-Variable.ttf#Noto Sans Gujarati, Segoe UI");
                }
            }
            else if (string.Equals(langCode, "hi-IN", StringComparison.OrdinalIgnoreCase))
            {
                FontFamily = new FontFamily("Nirmala UI, Segoe UI");
            }
            else
            {
                FontFamily = new FontFamily("Segoe UI");
            }
        }
        catch { }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || IsApplyingInput || IsTransliterationDisabled || !IsPhoneticEnabled)
            return;

        InputEngine?.HandlePreviewKeyDown(this, e);
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (e.Handled || IsApplyingInput || IsTransliterationDisabled || !IsPhoneticEnabled)
            return;

        InputEngine?.HandleCharacterReceived(this, e);
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (IsApplyingInput)
            return;

        InputEngine?.HandleSelectionChanged(this);
    }
}

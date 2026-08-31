using System;
using DhirDhar.Desktop.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Helpers;

/// <summary>
/// Forwarding adapter for backward compatibility with XAML using IndicInput attached properties.
/// Routes all registrations directly to the dedicated DhirDharInputEngine via DhirDharInput.
/// </summary>
public static class IndicInput
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(IndicInput),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty IsDisabledProperty =
        DependencyProperty.RegisterAttached(
            "IsDisabled",
            typeof(bool),
            typeof(IndicInput),
            new PropertyMetadata(false, OnIsDisabledChanged));

    public static readonly DependencyProperty TargetLanguageProperty =
        DependencyProperty.RegisterAttached(
            "TargetLanguage",
            typeof(string),
            typeof(IndicInput),
            new PropertyMetadata(string.Empty));

    public static bool GetIsEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsEnabledProperty, value);

    public static bool GetIsDisabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsDisabledProperty);

    public static void SetIsDisabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsDisabledProperty, value);

    public static string GetTargetLanguage(DependencyObject obj) =>
        (string)obj.GetValue(TargetLanguageProperty);

    public static void SetTargetLanguage(DependencyObject obj, string value) =>
        obj.SetValue(TargetLanguageProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        DhirDharInput.SetIsEnabled(d, (bool)e.NewValue);
    }

    private static void OnIsDisabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        DhirDharInput.SetIsDisabled(d, (bool)e.NewValue);
    }

    public static void Attach(TextBox textBox)
    {
        DhirDharInput.RegisterTextBox(textBox, InputFieldType.NaturalText);
    }

    public static void Detach(TextBox textBox)
    {
        DhirDharInput.UnregisterTextBox(textBox);
    }
}

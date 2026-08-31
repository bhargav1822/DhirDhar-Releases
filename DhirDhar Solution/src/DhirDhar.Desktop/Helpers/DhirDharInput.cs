using System;
using System.Runtime.CompilerServices;
using DhirDhar.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Helpers;

/// <summary>
/// Attached XAML behaviors for DhirDhar dedicated input system.
/// Connects UI controls directly to the central DhirDharInputEngine.
/// </summary>
public static class DhirDharInput
{
    public static readonly DependencyProperty FieldTypeProperty =
        DependencyProperty.RegisterAttached(
            "FieldType",
            typeof(InputFieldType),
            typeof(DhirDharInput),
            new PropertyMetadata(InputFieldType.NaturalText, OnFieldTypeChanged));

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(DhirDharInput),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty IsDisabledProperty =
        DependencyProperty.RegisterAttached(
            "IsDisabled",
            typeof(bool),
            typeof(DhirDharInput),
            new PropertyMetadata(false, OnIsDisabledChanged));

    private static readonly ConditionalWeakTable<TextBox, RoutedEventHandler> TextBoxLoadedHandlers = new();
    private static readonly ConditionalWeakTable<AutoSuggestBox, RoutedEventHandler> AutoSuggestLoadedHandlers = new();
    private static readonly ConditionalWeakTable<ComboBox, RoutedEventHandler> ComboBoxLoadedHandlers = new();

    public static InputFieldType GetFieldType(DependencyObject obj) =>
        (InputFieldType)obj.GetValue(FieldTypeProperty);

    public static void SetFieldType(DependencyObject obj, InputFieldType value) =>
        obj.SetValue(FieldTypeProperty, value);

    public static bool GetIsEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsEnabledProperty, value);

    public static bool GetIsDisabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsDisabledProperty);

    public static void SetIsDisabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsDisabledProperty, value);

    private static void OnFieldTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var type = (InputFieldType)e.NewValue;
        if (d is TextBox textBox)
        {
            AttachTextBox(textBox, type);
        }
        else if (d is AutoSuggestBox autoSuggestBox)
        {
            AttachAutoSuggestBox(autoSuggestBox, type);
        }
        else if (d is ComboBox comboBox)
        {
            AttachComboBox(comboBox, type);
        }
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        bool isEnabled = (bool)e.NewValue;
        if (isEnabled)
        {
            var type = GetFieldType(d);
            if (d is TextBox textBox)
            {
                AttachTextBox(textBox, type);
            }
            else if (d is AutoSuggestBox autoSuggestBox)
            {
                AttachAutoSuggestBox(autoSuggestBox, type);
            }
            else if (d is ComboBox comboBox)
            {
                AttachComboBox(comboBox, type);
            }
        }
        else
        {
            if (d is TextBox textBox)
            {
                DetachTextBox(textBox);
            }
            else if (d is AutoSuggestBox autoSuggestBox)
            {
                DetachAutoSuggestBox(autoSuggestBox);
            }
            else if (d is ComboBox comboBox)
            {
                DetachComboBox(comboBox);
            }
        }
    }

    private static void OnIsDisabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            if (d is TextBox textBox)
            {
                DetachTextBox(textBox);
            }
            else if (d is AutoSuggestBox autoSuggestBox)
            {
                DetachAutoSuggestBox(autoSuggestBox);
            }
            else if (d is ComboBox comboBox)
            {
                DetachComboBox(comboBox);
            }
        }
    }

    public static void AttachTextBox(TextBox textBox, InputFieldType type = InputFieldType.NaturalText)
    {
        if (textBox == null) return;
        RegisterTextBox(textBox, type);

        if (!TextBoxLoadedHandlers.TryGetValue(textBox, out _))
        {
            RoutedEventHandler loadedHandler = (s, e) =>
            {
                if (s is TextBox tb)
                {
                    RegisterTextBox(tb, GetFieldType(tb));
                }
            };
            TextBoxLoadedHandlers.AddOrUpdate(textBox, loadedHandler);
            textBox.Loaded += loadedHandler;
        }
    }

    public static void DetachTextBox(TextBox textBox)
    {
        if (textBox == null) return;
        if (TextBoxLoadedHandlers.TryGetValue(textBox, out var handler))
        {
            textBox.Loaded -= handler;
            TextBoxLoadedHandlers.Remove(textBox);
        }
        UnregisterTextBox(textBox);
    }

    public static void RegisterTextBox(TextBox textBox, InputFieldType type = InputFieldType.NaturalText)
    {
        if (textBox == null) return;
        try
        {
            var engine = App.ServiceProvider?.GetService<IDhirDharInputEngine>() ??
                         App.ServiceProvider?.GetService<IInputLanguageService>() as IDhirDharInputEngine;
            engine?.RegisterTextField(textBox, type);
        }
        catch { }
    }

    public static void UnregisterTextBox(TextBox textBox)
    {
        if (textBox == null) return;
        try
        {
            var engine = App.ServiceProvider?.GetService<IDhirDharInputEngine>() ??
                         App.ServiceProvider?.GetService<IInputLanguageService>() as IDhirDharInputEngine;
            engine?.UnregisterTextField(textBox);
        }
        catch { }
    }

    private static void AttachAutoSuggestBox(AutoSuggestBox autoSuggestBox, InputFieldType type)
    {
        void TryAttachInner()
        {
            var innerTextBox = FindVisualChild<TextBox>(autoSuggestBox);
            if (innerTextBox != null)
            {
                RegisterTextBox(innerTextBox, type);
            }
        }

        if (autoSuggestBox.IsLoaded)
        {
            TryAttachInner();
        }

        if (AutoSuggestLoadedHandlers.TryGetValue(autoSuggestBox, out var oldHandler))
        {
            autoSuggestBox.Loaded -= oldHandler;
        }
        autoSuggestBox.Unloaded -= OnAutoSuggestUnloaded;

        RoutedEventHandler loadedHandler = (s, e) => TryAttachInner();
        AutoSuggestLoadedHandlers.AddOrUpdate(autoSuggestBox, loadedHandler);
        autoSuggestBox.Loaded += loadedHandler;
        autoSuggestBox.Unloaded += OnAutoSuggestUnloaded;
    }

    private static void DetachAutoSuggestBox(AutoSuggestBox autoSuggestBox)
    {
        if (AutoSuggestLoadedHandlers.TryGetValue(autoSuggestBox, out var handler))
        {
            autoSuggestBox.Loaded -= handler;
            AutoSuggestLoadedHandlers.Remove(autoSuggestBox);
        }
        autoSuggestBox.Unloaded -= OnAutoSuggestUnloaded;

        var innerTextBox = FindVisualChild<TextBox>(autoSuggestBox);
        if (innerTextBox != null)
        {
            UnregisterTextBox(innerTextBox);
        }
    }

    private static void OnAutoSuggestUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is AutoSuggestBox autoSuggestBox)
        {
            DetachAutoSuggestBox(autoSuggestBox);
        }
    }

    private static void AttachComboBox(ComboBox comboBox, InputFieldType type)
    {
        void TryAttachInner()
        {
            var innerTextBox = FindVisualChild<TextBox>(comboBox);
            if (innerTextBox != null)
            {
                RegisterTextBox(innerTextBox, type);
            }
        }

        if (comboBox.IsLoaded)
        {
            TryAttachInner();
        }

        if (ComboBoxLoadedHandlers.TryGetValue(comboBox, out var oldHandler))
        {
            comboBox.Loaded -= oldHandler;
        }
        comboBox.Unloaded -= OnComboBoxUnloaded;

        RoutedEventHandler loadedHandler = (s, e) => TryAttachInner();
        ComboBoxLoadedHandlers.AddOrUpdate(comboBox, loadedHandler);
        comboBox.Loaded += loadedHandler;
        comboBox.Unloaded += OnComboBoxUnloaded;
    }

    private static void DetachComboBox(ComboBox comboBox)
    {
        if (ComboBoxLoadedHandlers.TryGetValue(comboBox, out var handler))
        {
            comboBox.Loaded -= handler;
            ComboBoxLoadedHandlers.Remove(comboBox);
        }
        comboBox.Unloaded -= OnComboBoxUnloaded;

        var innerTextBox = FindVisualChild<TextBox>(comboBox);
        if (innerTextBox != null)
        {
            UnregisterTextBox(innerTextBox);
        }
    }

    private static void OnComboBoxUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            DetachComboBox(comboBox);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }
            var result = FindVisualChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }
}

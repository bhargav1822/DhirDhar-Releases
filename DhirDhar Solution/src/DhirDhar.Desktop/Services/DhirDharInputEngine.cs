using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace DhirDhar.Desktop.Services;

/// <summary>
/// Authoritative, single-instance, application-local dedicated DhirDhar input system.
/// The ONLY service responsible for phonetic keyboard transliteration.
/// Completely isolated from Windows IME, keyboard layout changes, and external input services.
/// </summary>
public sealed class DhirDharInputEngine : IDhirDharInputEngine, IInputLanguageService
{
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<DhirDharInputEngine>? _logger;
    private readonly object _syncLock = new();

    private readonly Guid _instanceId = Guid.NewGuid();
    private InputServiceState _state = InputServiceState.Uninitialized;
    private bool _disposed;

    // Sub-engines
    private readonly GujaratiPhoneticEngine _gujaratiEngine = GujaratiPhoneticEngine.Instance;
    private readonly HindiPhoneticEngine _hindiEngine = HindiPhoneticEngine.Instance;
    private readonly EnglishInputEngine _englishEngine = EnglishInputEngine.Instance;

    private string _activeLanguage = "en-IN";
    private IPhoneticLanguageEngine _activeEngine = EnglishInputEngine.Instance;

    private readonly ConditionalWeakTable<TextBox, RegisteredFieldState> _registeredFields = new();
    private WeakReference<TextBox>? _activeTextBox;
    private bool _isProcessingInput;

    public DhirDharInputEngine(
        ILocalizationService localizationService,
        ILogger<DhirDharInputEngine>? logger = null)
    {
        _localizationService = localizationService;
        _logger = logger;
        InitializeOnce();
    }

    public Guid InstanceId => _instanceId;

    public InputServiceState State
    {
        get
        {
            lock (_syncLock)
            {
                return _state;
            }
        }
    }

    public string ActiveLanguage
    {
        get
        {
            lock (_syncLock)
            {
                return _activeLanguage;
            }
        }
    }

    public IPhoneticLanguageEngine ActiveLanguageEngine
    {
        get
        {
            lock (_syncLock)
            {
                return _activeEngine;
            }
        }
    }

    public TextBox? ActiveTextBox
    {
        get
        {
            lock (_syncLock)
            {
                if (_activeTextBox != null && _activeTextBox.TryGetTarget(out var textBox))
                {
                    return textBox;
                }
                return null;
            }
        }
    }

    public InputFieldType? ActiveFieldType
    {
        get
        {
            var tb = ActiveTextBox;
            if (tb != null && _registeredFields.TryGetValue(tb, out var fieldState))
            {
                return fieldState.FieldType;
            }
            return null;
        }
    }

    public string InputBuffer
    {
        get
        {
            var tb = ActiveTextBox;
            if (tb != null && _registeredFields.TryGetValue(tb, out var fieldState))
            {
                return fieldState.LatinBuffer;
            }
            return string.Empty;
        }
    }

    public string CurrentWordBuffer => InputBuffer;

    public InputCompositionState CompositionState
    {
        get
        {
            var tb = ActiveTextBox;
            if (tb != null && _registeredFields.TryGetValue(tb, out var fieldState))
            {
                return fieldState.IsComposing ? InputCompositionState.Composing : InputCompositionState.Inactive;
            }
            return InputCompositionState.Inactive;
        }
    }

    public int CaretPosition => ActiveTextBox?.SelectionStart ?? 0;

    public int SelectionStart => ActiveTextBox?.SelectionStart ?? 0;

    public int SelectionLength => ActiveTextBox?.SelectionLength ?? 0;

    public bool IsProcessingInput => _isProcessingInput;

    public bool IsComposing => CompositionState == InputCompositionState.Composing;

    // Events
    public event EventHandler<string>? LanguageChanged;
    public event EventHandler<TextBox?>? ActiveTextBoxChanged;
    public event EventHandler<InputLanguageChangedEventArgs>? InputLanguageChanged;
    public event EventHandler<IndicInputMode>? InputModeChanged;
    public event EventHandler<FrameworkElement?>? TargetChanged;

    // IInputLanguageService compatibility properties
    public InputLanguageInfo Current => new(ActiveLanguage, _activeEngine.LanguageName, IntPtr.Zero, true);
    public string TargetLanguage => ScriptTranslator.NormalizeLanguageCode(ActiveLanguage);
    public bool IsIndicActive => _activeEngine.IsPhoneticActive;
    public IndicInputMode CurrentMode
    {
        get => IsIndicActive ? IndicInputMode.IndicPhonetic : IndicInputMode.EnglishLatin;
        set { }
    }
    public FrameworkElement? CurrentTarget => ActiveTextBox;

    public void InitializeOnce()
    {
        lock (_syncLock)
        {
            if (_state == InputServiceState.Ready || _state == InputServiceState.Disposed)
            {
                return;
            }

            try
            {
                _state = InputServiceState.Initializing;
                _localizationService.LanguageChanged += OnLocalizationLanguageChanged;

                var initialLang = _localizationService.CurrentLanguage ?? "en-IN";
                SelectEngineForLanguage(initialLang, isInitial: true);
                _state = InputServiceState.Ready;

                _logger?.LogInformation("[INPUT] DhirDharInputEngine initialized (InstanceId={InstanceId}, Language={Language})", _instanceId, _activeLanguage);
            }
            catch (Exception ex)
            {
                _state = InputServiceState.Failed;
                _logger?.LogError(ex, "[INPUT] Error during DhirDharInputEngine initialization.");
            }
        }
    }

    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return;

        lock (_syncLock)
        {
            if (_disposed) return;
            SelectEngineForLanguage(languageCode, isInitial: false);
        }
    }

    private void SelectEngineForLanguage(string languageCode, bool isInitial)
    {
        var canonical = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeLanguageCode(languageCode);
        var normalized = ScriptTranslator.NormalizeLanguageCode(canonical);

        if (!isInitial && string.Equals(_activeLanguage, canonical, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string oldLang = _activeLanguage;
        _activeLanguage = canonical;

        // Select exactly ONE dedicated engine
        if (normalized == "gu")
        {
            _activeEngine = _gujaratiEngine;
        }
        else if (normalized == "hi" || normalized == "mr")
        {
            _activeEngine = _hindiEngine;
        }
        else
        {
            _activeEngine = _englishEngine;
        }

        // Commit active composition in current target
        CommitComposition();

        _logger?.LogInformation("DhirDharInputEngine LanguageChange={OldLang}→{NewLang}", oldLang, canonical);
        _logger?.LogInformation("DhirDharInputEngine Language={Lang} Engine={Engine} State=Active", canonical, _activeEngine.GetType().Name);

        LanguageChanged?.Invoke(this, canonical);
        InputLanguageChanged?.Invoke(this, new InputLanguageChangedEventArgs(Current));
        InputModeChanged?.Invoke(this, CurrentMode);
    }

    public void RegisterTextField(TextBox textBox, InputFieldType type = InputFieldType.NaturalText)
    {
        if (textBox == null) return;

        lock (_syncLock)
        {
            if (_disposed) return;

            if (_registeredFields.TryGetValue(textBox, out var existing))
            {
                existing.FieldType = type;
                EnsureSubscribed(textBox, existing);
                return;
            }

            var fieldState = new RegisteredFieldState(textBox, type);
            _registeredFields.Add(textBox, fieldState);
            EnsureSubscribed(textBox, fieldState);

            _logger?.LogDebug("DhirDharInputEngine FieldRegistered Type={FieldType}", type);
        }
    }

    private void EnsureSubscribed(TextBox textBox, RegisteredFieldState fieldState)
    {
        if (textBox is DhirDhar.Desktop.Controls.DhirDharPhoneticTextBox)
        {
            // Dedicated DhirDharPhoneticTextBox dispatches directly to IDhirDharInputEngine.
            // Avoid duplicate subscriptions to the control's PreviewKeyDown/CharacterReceived/SelectionChanged events.
            fieldState.IsSubscribed = true;
            return;
        }

        if (!fieldState.IsSubscribed)
        {
            textBox.PreviewKeyDown -= OnTextBoxPreviewKeyDown;
            textBox.PreviewKeyDown += OnTextBoxPreviewKeyDown;
            textBox.CharacterReceived -= OnTextBoxCharacterReceived;
            textBox.CharacterReceived += OnTextBoxCharacterReceived;
            textBox.SelectionChanged -= OnTextBoxSelectionChanged;
            textBox.SelectionChanged += OnTextBoxSelectionChanged;
            textBox.GotFocus -= OnTextBoxGotFocus;
            textBox.GotFocus += OnTextBoxGotFocus;
            textBox.LostFocus -= OnTextBoxLostFocus;
            textBox.LostFocus += OnTextBoxLostFocus;
            fieldState.IsSubscribed = true;
        }
    }

    public void UnregisterTextField(TextBox textBox)
    {
        if (textBox == null) return;

        lock (_syncLock)
        {
            if (_registeredFields.TryGetValue(textBox, out var fieldState))
            {
                if (fieldState.IsSubscribed && textBox is not DhirDhar.Desktop.Controls.DhirDharPhoneticTextBox)
                {
                    textBox.PreviewKeyDown -= OnTextBoxPreviewKeyDown;
                    textBox.CharacterReceived -= OnTextBoxCharacterReceived;
                    textBox.SelectionChanged -= OnTextBoxSelectionChanged;
                    textBox.GotFocus -= OnTextBoxGotFocus;
                    textBox.LostFocus -= OnTextBoxLostFocus;
                    fieldState.IsSubscribed = false;
                }

                fieldState.Reset();
                _registeredFields.Remove(textBox);
            }

            if (_activeTextBox != null && _activeTextBox.TryGetTarget(out var active) && ReferenceEquals(active, textBox))
            {
                _activeTextBox = null;
                ActiveTextBoxChanged?.Invoke(this, null);
                TargetChanged?.Invoke(this, null);
            }
        }
    }

    public void SetTarget(TextBox? textBox)
    {
        lock (_syncLock)
        {
            if (_disposed) return;

            if (ReferenceEquals(ActiveTextBox, textBox)) return;

            _activeTextBox = textBox != null ? new WeakReference<TextBox>(textBox) : null;
            ActiveTextBoxChanged?.Invoke(this, textBox);
            TargetChanged?.Invoke(this, textBox);
        }
    }

    public void SetTarget(FrameworkElement? control)
    {
        SetTarget(control as TextBox);
    }

    public void RegisterTextBox(TextBox textBox)
    {
        RegisterTextField(textBox, InputFieldType.NaturalText);
    }

    public void UnregisterTextBox(TextBox textBox)
    {
        UnregisterTextField(textBox);
    }

    public string Transliterate(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;
        lock (_syncLock)
        {
            return _activeEngine.Transliterate(input);
        }
    }

    public string TransliterateWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return word ?? string.Empty;
        lock (_syncLock)
        {
            return _activeEngine.TransliterateWord(word);
        }
    }

    public void CommitComposition(TextBox? textBox = null)
    {
        var target = textBox ?? ActiveTextBox;
        if (target != null && _registeredFields.TryGetValue(target, out var fieldState))
        {
            fieldState.Commit();
        }
    }

    public void CommitActiveComposition(TextBox? textBox = null)
    {
        CommitComposition(textBox);
    }

    public void HandlePreviewKeyDown(TextBox textBox, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        OnTextBoxPreviewKeyDown(textBox, e);
    }

    public void HandleCharacterReceived(TextBox textBox, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs e)
    {
        OnTextBoxCharacterReceived(textBox, e);
    }

    public void HandleSelectionChanged(TextBox textBox)
    {
        OnTextBoxSelectionChanged(textBox, new Microsoft.UI.Xaml.RoutedEventArgs());
    }

    public void ResetState()
    {
        var target = ActiveTextBox;
        if (target != null && _registeredFields.TryGetValue(target, out var fieldState))
        {
            fieldState.Reset();
        }
    }

    public void RegisterWindow(IntPtr hwnd)
    {
        // Zero-op: isolated application-local input engine requires no window hooks
    }

    public void Refresh()
    {
        lock (_syncLock)
        {
            if (_disposed) return;
            SetLanguage(_localizationService.CurrentLanguage ?? "en-IN");
        }
    }

    private void OnLocalizationLanguageChanged(object? sender, EventArgs e)
    {
        SetLanguage(_localizationService.CurrentLanguage ?? "en-IN");
    }

    // =========================================================================
    // KEYBOARD INPUT & COMPOSITION PIPELINE
    // =========================================================================

    private void OnTextBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        
        // Ensure registration is always attached when receiving focus
        if (!_registeredFields.TryGetValue(textBox, out var fieldState))
        {
            RegisterTextField(textBox, InputFieldType.NaturalText);
            _registeredFields.TryGetValue(textBox, out fieldState);
        }

        SetTarget(textBox);
        fieldState?.Reset();

        _logger?.LogDebug("DhirDharInputEngine Language={Lang} Engine={Engine} Field={Field} State=Active Event=GotFocus",
            _activeLanguage, _activeEngine.GetType().Name, fieldState?.FieldType);
    }

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (_registeredFields.TryGetValue(textBox, out var fieldState))
        {
            fieldState.Commit();
        }
    }

    private void OnTextBoxSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (!_registeredFields.TryGetValue(textBox, out var fieldState)) return;

        if (fieldState.IsApplyingText || fieldState.SuppressSelectionCommit > 0) return;

        if (fieldState.IsComposing)
        {
            int selStart = textBox.SelectionStart;
            // Only commit if selection moved completely outside the active composition span
            if (selStart < fieldState.CompStart || selStart > fieldState.CompStart + fieldState.CompLength)
            {
                fieldState.Commit();
            }
        }
    }

    private void OnTextBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;
        if (sender is not TextBox textBox) return;
        if (!_registeredFields.TryGetValue(textBox, out var fieldState)) return;

        _logger?.LogDebug("[INPUT] KeyEventReceived Key={Key} Handled={Handled} Field={Field}", e.Key, e.Handled, fieldState.FieldType);

        // Phonetic typing only eligible for NaturalText and SearchText fields
        if (fieldState.FieldType != InputFieldType.NaturalText && fieldState.FieldType != InputFieldType.SearchText)
        {
            fieldState.HandledInPreviewKeyDown = false;
            return;
        }

        // If active engine is English or has transliteration disabled, pass through
        if (!_activeEngine.IsPhoneticActive)
        {
            fieldState.Reset();
            fieldState.HandledInPreviewKeyDown = false;
            return;
        }

        bool isCtrlDown = IsKeyDown(VirtualKey.Control);
        bool isAltDown = IsKeyDown(VirtualKey.Menu);
        bool isShiftDown = IsKeyDown(VirtualKey.Shift);
        bool isCapsLock = IsKeyLocked(VirtualKey.CapitalLock);

        // 1. Handle Paste (Ctrl+V)
        if (isCtrlDown && !isAltDown && e.Key == VirtualKey.V)
        {
            e.Handled = true;
            fieldState.HandledInPreviewKeyDown = true;
            _logger?.LogDebug("[INPUT] EventConsumed=true EngineProcessed=true Action=Paste");
            _ = HandlePasteAsync(textBox, fieldState);
            return;
        }

        // 2. Pass through standard shortcuts (Ctrl+A, Ctrl+C, Ctrl+Z, Ctrl+X)
        if (isCtrlDown || isAltDown)
        {
            if (e.Key != VirtualKey.Back && e.Key != VirtualKey.Delete)
            {
                fieldState.Commit();
                fieldState.HandledInPreviewKeyDown = false;
                return;
            }
        }

        // 3. Backspace Key
        if (e.Key == VirtualKey.Back)
        {
            e.Handled = true;
            fieldState.HandledInPreviewKeyDown = true;
            bool isCtrl = isCtrlDown;
            if (fieldState.IsComposing)
            {
                HandleBackspaceForComposing(textBox, fieldState);
            }
            else
            {
                HandleBackspaceForCommitted(textBox, fieldState, isCtrl);
            }

            _logger?.LogDebug("[INPUT] EventConsumed=true EngineProcessed=true Edit=Backspace Language={Lang} Engine={Engine} Field={Field} Caret={Caret}",
                _activeLanguage, _activeEngine.GetType().Name, fieldState.FieldType, textBox.SelectionStart);
            return;
        }

        // 4. Delete Key
        if (e.Key == VirtualKey.Delete)
        {
            e.Handled = true;
            fieldState.HandledInPreviewKeyDown = true;
            bool isCtrl = isCtrlDown;
            if (fieldState.IsComposing)
            {
                fieldState.Commit();
            }
            HandleDeleteForCommitted(textBox, fieldState, isCtrl);

            _logger?.LogDebug("[INPUT] EventConsumed=true EngineProcessed=true Edit=Delete Language={Lang} Engine={Engine} Field={Field} Caret={Caret}",
                _activeLanguage, _activeEngine.GetType().Name, fieldState.FieldType, textBox.SelectionStart);
            return;
        }

        // 5. Enter Key
        if (e.Key == VirtualKey.Enter)
        {
            if (fieldState.IsComposing)
            {
                fieldState.Commit();
            }
            fieldState.HandledInPreviewKeyDown = false;
            return;
        }

        // 6. Space Key - commits active word buffer and deterministically inserts space while remaining ACTIVE
        if (e.Key == VirtualKey.Space)
        {
            e.Handled = true;
            fieldState.HandledInPreviewKeyDown = true;
            HandleSpaceKey(textBox, fieldState);
            _logger?.LogDebug("[INPUT] EventConsumed=true EngineProcessed=true Edit=Space Language={Lang} Engine={Engine} Field={Field} Caret={Caret}",
                _activeLanguage, _activeEngine.GetType().Name, fieldState.FieldType, textBox.SelectionStart);
            return;
        }

        // 7. Special modifier characters (^ Anusvara, _ Halant, ~ Half Consonant)
        if (isShiftDown && e.Key == VirtualKey.Number6)
        {
            e.Handled = true;
            fieldState.HandledInPreviewKeyDown = true;
            ProcessTypedCharacter('^', textBox, fieldState);
            return;
        }
        if (isShiftDown && (int)e.Key == 189) // OemMinus
        {
            e.Handled = true;
            fieldState.HandledInPreviewKeyDown = true;
            ProcessTypedCharacter('_', textBox, fieldState);
            return;
        }
        if (isShiftDown && (int)e.Key == 192) // OemTilde
        {
            e.Handled = true;
            fieldState.HandledInPreviewKeyDown = true;
            ProcessTypedCharacter('~', textBox, fieldState);
            return;
        }

        // 8. Number Keys (0-9, Numpad 0-9) - bypass phonetic composition
        if ((!isShiftDown && e.Key >= VirtualKey.Number0 && e.Key <= VirtualKey.Number9) ||
            (e.Key >= VirtualKey.NumberPad0 && e.Key <= VirtualKey.NumberPad9))
        {
            if (fieldState.IsComposing)
            {
                fieldState.Commit();
            }
            fieldState.HandledInPreviewKeyDown = false;
            return;
        }

        // 9. Letter Keys (A-Z)
        if (e.Key >= VirtualKey.A && e.Key <= VirtualKey.Z)
        {
            bool isUpper = isShiftDown ^ isCapsLock;
            char baseChar = (char)('a' + (e.Key - VirtualKey.A));
            char typedChar = isUpper ? char.ToUpperInvariant(baseChar) : baseChar;

            e.Handled = true;
            fieldState.HandledInPreviewKeyDown = true;
            ProcessTypedCharacter(typedChar, textBox, fieldState);
            return;
        }

        // 10. Navigation / Tab / Escape
        if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Home or VirtualKey.End or VirtualKey.Escape or VirtualKey.Tab)
        {
            fieldState.Commit();
            fieldState.HandledInPreviewKeyDown = false;
            return;
        }

        // 11. Punctuation / other symbols - commit composition
        fieldState.Commit();
        fieldState.HandledInPreviewKeyDown = false;
    }

    private void OnTextBoxCharacterReceived(object sender, CharacterReceivedRoutedEventArgs e)
    {
        if (e.Handled) return;
        if (sender is not TextBox textBox) return;
        if (!_registeredFields.TryGetValue(textBox, out var fieldState)) return;

        _logger?.LogDebug("[INPUT] CharacterReceived Char='{Char}' HandledInPreviewKeyDown={HandledInPreviewKeyDown}",
            e.Character, fieldState.HandledInPreviewKeyDown);

        if (fieldState.HandledInPreviewKeyDown)
        {
            e.Handled = true;
            fieldState.HandledInPreviewKeyDown = false;
            _logger?.LogDebug("[INPUT] EventConsumed=true ProgrammaticUpdate=false TextChangedIgnored=true Reason=ConsumedByPreviewKeyDown");
            return;
        }

        if (!_activeEngine.IsPhoneticActive)
        {
            return;
        }

        if (fieldState.FieldType == InputFieldType.NaturalText || fieldState.FieldType == InputFieldType.SearchText)
        {
            if (e.Character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '^' or '_' or '~')
            {
                e.Handled = true;
                _logger?.LogDebug("[INPUT] EventConsumed=true EngineProcessed=true Action=ProcessTypedCharFromCharReceived Char='{Char}'", e.Character);
                ProcessTypedCharacter(e.Character, textBox, fieldState);
            }
        }
    }

    private void HandleSpaceKey(TextBox textBox, RegisteredFieldState state)
    {
        state.Commit();

        string currentText = textBox.Text ?? string.Empty;
        int selStart = textBox.SelectionStart;
        int selLength = textBox.SelectionLength;

        string newText;
        if (selLength > 0 && selStart < currentText.Length)
        {
            newText = currentText.Remove(selStart, Math.Min(selLength, currentText.Length - selStart)).Insert(selStart, " ");
        }
        else
        {
            newText = currentText.Insert(Math.Min(selStart, currentText.Length), " ");
        }

        int newCaret = Math.Min(newText.Length, selStart + 1);
        SetTextBoxText(textBox, state, newText, newCaret);
        state.CompStart = newCaret;
        state.CompLength = 0;
    }

    private void ProcessTypedCharacter(char c, TextBox textBox, RegisteredFieldState state)
    {
        string currentText = textBox.Text ?? string.Empty;

        if (!state.IsComposing)
        {
            int selStart = textBox.SelectionStart;
            int selLength = textBox.SelectionLength;

            if (selLength > 0 && selStart < currentText.Length)
            {
                currentText = currentText.Remove(selStart, Math.Min(selLength, currentText.Length - selStart));
            }

            state.CompStart = selStart;
            state.CompLength = 0;
            state.LatinBuffer = c.ToString();
        }
        else
        {
            state.LatinBuffer += c;
        }

        string transliterated = _activeEngine.TransliterateWord(state.LatinBuffer);

        _logger?.LogDebug("[INPUT] EngineProcessed=true Language={Lang} Engine={Engine} Field={Field} LatinBuf='{Buf}' Transliterated='{Transliterated}' Caret={Caret}",
            _activeLanguage, _activeEngine.GetType().Name, state.FieldType, state.LatinBuffer, transliterated, state.CompStart + state.CompLength);

        ApplyTransliteration(textBox, state, currentText, transliterated);
    }

    private void HandleBackspaceForComposing(TextBox textBox, RegisteredFieldState state)
    {
        string currentText = textBox.Text ?? string.Empty;

        if (state.LatinBuffer.Length > 1)
        {
            state.LatinBuffer = state.LatinBuffer[..^1];
            string transliterated = _activeEngine.TransliterateWord(state.LatinBuffer);
            ApplyTransliteration(textBox, state, currentText, transliterated);
        }
        else
        {
            state.LatinBuffer = string.Empty;
            if (state.CompStart <= currentText.Length && state.CompLength > 0)
            {
                int removeLen = Math.Min(state.CompLength, currentText.Length - state.CompStart);
                currentText = currentText.Remove(state.CompStart, removeLen);
            }

            int targetCaret = state.CompStart;
            state.CompLength = 0;

            SetTextBoxText(textBox, state, currentText, targetCaret);
        }
    }

    private void HandleBackspaceForCommitted(TextBox textBox, RegisteredFieldState state, bool isCtrl)
    {
        try
        {
            string text = textBox.Text ?? string.Empty;
            int selStart = textBox.SelectionStart;
            int selLen = textBox.SelectionLength;
            state.Commit();

            string newText;
            int newCaret;

            if (selLen > 0)
            {
                newText = text.Remove(selStart, selLen);
                newCaret = selStart;
            }
            else if (isCtrl)
            {
                int wordStart = FindPreviousWordBoundary(text, selStart);
                int deleteLen = selStart - wordStart;
                if (deleteLen <= 0) return;
                newText = text.Remove(wordStart, deleteLen);
                newCaret = wordStart;
            }
            else
            {
                if (selStart == 0) return;
                int prevBoundary = GetPreviousCharacterBoundary(text, selStart);
                int deleteLen = selStart - prevBoundary;
                if (deleteLen <= 0) return;
                newText = text.Remove(prevBoundary, deleteLen);
                newCaret = prevBoundary;
            }

            SetTextBoxText(textBox, state, newText, newCaret);
        }
        catch { }
    }

    private void HandleDeleteForCommitted(TextBox textBox, RegisteredFieldState state, bool isCtrl)
    {
        try
        {
            string text = textBox.Text ?? string.Empty;
            int selStart = textBox.SelectionStart;
            int selLen = textBox.SelectionLength;
            state.Commit();

            string newText;
            int newCaret = selStart;

            if (selLen > 0)
            {
                newText = text.Remove(selStart, selLen);
            }
            else if (isCtrl)
            {
                int wordEnd = FindNextWordBoundary(text, selStart);
                int deleteLen = wordEnd - selStart;
                if (deleteLen <= 0) return;
                newText = text.Remove(selStart, deleteLen);
            }
            else
            {
                if (selStart >= text.Length) return;
                int nextBoundary = GetNextCharacterBoundary(text, selStart);
                int deleteLen = nextBoundary - selStart;
                if (deleteLen <= 0) return;
                newText = text.Remove(selStart, deleteLen);
            }

            SetTextBoxText(textBox, state, newText, newCaret);
        }
        catch { }
    }

    private void ApplyTransliteration(TextBox textBox, RegisteredFieldState state, string currentText, string transliterated)
    {
        int removeLen = Math.Min(state.CompLength, Math.Max(0, currentText.Length - state.CompStart));
        string newText = currentText.Remove(state.CompStart, removeLen).Insert(state.CompStart, transliterated);
        state.CompLength = transliterated.Length;

        SetTextBoxText(textBox, state, newText, state.CompStart + state.CompLength);
    }

    private void SetTextBoxText(TextBox textBox, RegisteredFieldState state, string newText, int newCaret)
    {
        _isProcessingInput = true;
        state.IsApplyingText = true;
        state.SuppressSelectionCommit++;

        var phoneticBox = textBox as DhirDhar.Desktop.Controls.DhirDharPhoneticTextBox;
        if (phoneticBox != null)
        {
            phoneticBox.IsApplyingInput = true;
        }

        try
        {
            textBox.Text = newText;
            textBox.SelectionStart = Math.Max(0, Math.Min(newText.Length, newCaret));
            textBox.SelectionLength = 0;

            _logger?.LogDebug("[INPUT] ProgrammaticUpdate=true Text='{Text}' Caret={Caret}", newText, textBox.SelectionStart);
        }
        finally
        {
            if (phoneticBox != null)
            {
                phoneticBox.IsApplyingInput = false;
            }

            _isProcessingInput = false;
            state.IsApplyingText = false;
            state.SuppressSelectionCommit--;
        }
    }

    private async Task HandlePasteAsync(TextBox textBox, RegisteredFieldState state)
    {
        try
        {
            state.Commit();
            var clip = Clipboard.GetContent();
            if (clip != null && clip.Contains(StandardDataFormats.Text))
            {
                var text = await clip.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    string transliterated;
                    bool isIndic = ScriptTranslator.IsIndicScript(text) && !ScriptTranslator.ContainsLatinLetters(text);
                    bool isLatin = ScriptTranslator.ContainsLatinLetters(text);

                    if (isIndic)
                    {
                        transliterated = text;
                    }
                    else if (isLatin && _activeEngine.IsPhoneticActive)
                    {
                        transliterated = _activeEngine.Transliterate(text);
                    }
                    else
                    {
                        transliterated = text;
                    }

                    int start = textBox.SelectionStart;
                    int length = textBox.SelectionLength;
                    string currentText = textBox.Text ?? string.Empty;

                    string newText = currentText.Remove(start, Math.Min(length, Math.Max(0, currentText.Length - start)))
                                               .Insert(start, transliterated);

                    SetTextBoxText(textBox, state, newText, start + transliterated.Length);
                }
            }
        }
        catch { }
    }

    private static int GetPreviousCharacterBoundary(string text, int caretPos)
    {
        if (caretPos <= 0 || string.IsNullOrEmpty(text)) return 0;
        if (caretPos > text.Length) caretPos = text.Length;

        // Check if character before caret is a surrogate pair
        if (caretPos >= 2 && char.IsSurrogatePair(text[caretPos - 2], text[caretPos - 1]))
        {
            return caretPos - 2;
        }

        return caretPos - 1;
    }

    private static int GetNextCharacterBoundary(string text, int caretPos)
    {
        if (caretPos >= text.Length || string.IsNullOrEmpty(text)) return text.Length;

        if (caretPos + 1 < text.Length && char.IsSurrogatePair(text[caretPos], text[caretPos + 1]))
        {
            return caretPos + 2;
        }

        return caretPos + 1;
    }

    private static int FindPreviousWordBoundary(string text, int caretPos)
    {
        if (caretPos == 0) return 0;
        int pos = caretPos;
        while (pos > 0 && char.IsWhiteSpace(text[pos - 1])) pos--;
        while (pos > 0 && !char.IsWhiteSpace(text[pos - 1])) pos--;
        return pos;
    }

    private static int FindNextWordBoundary(string text, int caretPos)
    {
        if (caretPos >= text.Length) return text.Length;
        int pos = caretPos;
        while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
        while (pos < text.Length && !char.IsWhiteSpace(text[pos])) pos++;
        return pos;
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    private static bool IsKeyLocked(VirtualKey key)
    {
        var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return (state & CoreVirtualKeyStates.Locked) == CoreVirtualKeyStates.Locked;
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed) return;
            _disposed = true;
            _state = InputServiceState.Disposed;

            try
            {
                _localizationService.LanguageChanged -= OnLocalizationLanguageChanged;
            }
            catch { }

            _activeTextBox = null;
            _logger?.LogInformation("[INPUT] DhirDharInputEngine disposed (InstanceId={InstanceId})", _instanceId);
        }
    }

    /// <summary>
    /// Encapsulates per-TextBox composition and registration state.
    /// </summary>
    private sealed class RegisteredFieldState
    {
        public TextBox TextBox { get; }
        public InputFieldType FieldType { get; set; }
        public string LatinBuffer { get; set; } = string.Empty;
        public int CompStart { get; set; }
        public int CompLength { get; set; }
        public bool IsApplyingText { get; set; }
        public int SuppressSelectionCommit { get; set; }
        public bool IsSubscribed { get; set; }
        public bool HandledInPreviewKeyDown { get; set; }

        public bool IsComposing => !string.IsNullOrEmpty(LatinBuffer);

        public RegisteredFieldState(TextBox textBox, InputFieldType fieldType)
        {
            TextBox = textBox;
            FieldType = fieldType;
        }

        public void Commit()
        {
            LatinBuffer = string.Empty;
            CompLength = 0;
            HandledInPreviewKeyDown = false;
        }

        public void Reset()
        {
            LatinBuffer = string.Empty;
            CompStart = 0;
            CompLength = 0;
            IsApplyingText = false;
            SuppressSelectionCommit = 0;
            HandledInPreviewKeyDown = false;
        }
    }
}

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OmniControl.App.Hotkeys;

namespace OmniControl.App.Picker;

/// <summary>
/// The master picker's three panes: sections, the entries of the section, and the settings of the highlighted entry (always shown, live).
/// While the keys are in the list, focus never leaves the search box, so typing always searches; the arrow keys move the highlight
/// (Up/Down in the list, Left/Right between sections while the box is empty) and Tab moves into the settings. In the settings Up/Down choose
/// a setting and Left/Right change it. Enter uses the highlighted entry from anywhere; Esc leaves the settings, clears the search, then closes.
/// </summary>
public partial class PickerMenu : UserControl
{
    private PickerModel? _model;

    /// <summary>An entry was chosen, with the values of its settings.</summary>
    public event Action<PickResult>? Picked;

    /// <summary>The user asked to close the window (Esc with nothing typed and the keys in the list).</summary>
    public event Action? CloseRequested;

    public PickerMenu()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel);
        OptionList.AddHandler(PointerWheelChangedEvent, OnOptionWheel);
        ActionList.AddHandler(DoubleTappedEvent, OnActionDoubleTapped);
        SectionList.AddHandler(TappedEvent, OnSectionTapped);
    }

    /// <summary>The search box (the window holds the keys from Windows while it has them, so a pressed key is searched for).</summary>
    internal TextBox SearchBox => Search;

    /// <summary>Starts a fresh pick: shows the model and puts the cursor where typing or the arrow keys belong.</summary>
    public void Show(PickerModel model)
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = model;
        model.PropertyChanged += OnModelChanged;
        DataContext = model;
        Dispatcher.UIThread.Post(() =>
        {
            if (model.StartInSettings) model.EnterSettings();
            FocusCurrentPane();
            SectionList.ScrollIntoView(model.SectionIndex);
            ActionList.ScrollIntoView(model.ActionIndex);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Lets go of the model when the window closes.</summary>
    public void Release()
    {
        _model?.Release();
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = null;
    }

    private void FocusCurrentPane()
    {
        if (_model?.InSettings == true) SettingsFocus.Focus(NavigationMethod.Directional);
        else Search.Focus(NavigationMethod.Directional);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model is null) return;
        switch (e.PropertyName)
        {
            case nameof(PickerModel.ActionIndex) when _model.ActionIndex >= 0: ActionList.ScrollIntoView(_model.ActionIndex); break;
            case nameof(PickerModel.SectionIndex) when _model.SectionIndex >= 0: SectionList.ScrollIntoView(_model.SectionIndex); break;
            case nameof(PickerModel.OptionIndex) when _model.OptionIndex >= 0: OptionList.ScrollIntoView(_model.OptionIndex); break;
            case nameof(PickerModel.InSettings): Dispatcher.UIThread.Post(FocusCurrentPane, DispatcherPriority.Loaded); break;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is not { } model) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control && !HotkeyCapture.IsListening(focused))
        {
            FocusSearch(model);
            e.Handled = true;
            return;
        }
        if (focused is Visual inHotkeys && HotkeyPart.IsVisualAncestorOf(inHotkeys))
        {
            if (OnHotkeyPartKey(model, focused, e)) e.Handled = true;
            return;
        }
        // A click into a settings box puts the keys there too.
        var inSettings = model.InSettings || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox box && box != Search;
        var handled = inSettings ? OnSettingsKey(model, e) : OnListKey(model, e);
        if (handled) e.Handled = true;
    }

    /// <summary>Uses the highlighted entry (a list's button runs instead). Nothing happens when it can't be used.</summary>
    private void UseHighlighted(PickerModel model)
    {
        // A text being typed is taken first (a text setting commits when its box loses focus).
        if (model.IsManaging && model.ActiveChoice is { Command: null })
        {
            HotkeyStops().FirstOrDefault()?.Focus(NavigationMethod.Tab); // nothing is picked here: Enter goes to the keys of the entry
            return;
        }
        SettingsFocus.Focus(NavigationMethod.Directional);
        if (model.ActiveChoice is { Command: not null } command) _ = model.RunCommandAsync(command, this);
        else if (model.Use() is { } pick) Picked?.Invoke(pick);
        else FocusCurrentPane();
    }

    /// <summary>Keys while they are in the list. Returns true when the key was used.</summary>
    private bool OnListKey(PickerModel model, KeyEventArgs e)
    {
        var typing = model.Query.Length > 0; // with text in the box, Left/Right belong to the caret
        if (model.HasKeys && HotkeyCapture.SearchChordOf(e) is { } chord)
        {
            model.Query = chord; // the actions on that key
            Search.CaretIndex = chord.Length;
            return true;
        }

        switch (e.Key)
        {
            case Key.Up or Key.Down:
                model.Move(Services.Wrap.Direction(e.Key));
                return true;
            case Key.Left or Key.Right when !typing:
                model.MoveSection(e.Key == Key.Right ? 1 : -1);
                return true;
            case Key.Enter:
                UseHighlighted(model);
                return true;
            case Key.Tab:
                if (model.HasHotkeys) HotkeyStops().FirstOrDefault()?.Focus(NavigationMethod.Tab);
                else model.EnterSettings(); // the only stop is the search box, so Tab is free to move into the settings
                return true;
            case Key.Escape:
                if (typing) model.Query = string.Empty;
                else CloseRequested?.Invoke();
                return true;
        }
        return false;
    }

    /// <summary>Keys while they are in the settings. Returns true when the key was used.</summary>
    private bool OnSettingsKey(PickerModel model, KeyEventArgs e)
    {
        var inText = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

        switch (e.Key)
        {
            case Key.Up or Key.Down:
                if (inText) SettingsFocus.Focus(NavigationMethod.Directional);
                model.MoveOption(Services.Wrap.Direction(e.Key));
                return true;
            case Key.Left or Key.Right when !inText:
                model.SelectedOption?.Step(e.Key == Key.Right ? 1 : -1);
                return true;
            case Key.Space when !inText:
                if (model.OnFootButton) model.RunFootButton();
                else model.SelectedOption?.Flip();
                return true;
            case Key.Enter:
                if (model.OnFootButton) model.RunFootButton();
                else if (inText) SettingsFocus.Focus(NavigationMethod.Directional); // done typing (a typed number is taken when the box loses focus)
                else if (model.SelectedOption is { IsText: true } or { IsPath: true } && e.KeyModifiers != KeyModifiers.Control) EditText();
                else if (model.SelectedOption is { IsNumber: true } number && e.KeyModifiers == KeyModifiers.Control) BeginTyping(number); // Enter itself applies
                else UseHighlighted(model);
                return true;
            case Key.Tab:
                SettingsFocus.Focus(NavigationMethod.Directional); // takes a typed text before the keys go back to the list
                model.InSettings = false;
                return true;
            case Key.Escape:
                if (inText)
                {
                    model.SelectedOption?.CancelTyping();
                    SettingsFocus.Focus(NavigationMethod.Directional);
                }
                else model.InSettings = false;
                return true;
        }
        return false;
    }

    /// <summary>Ctrl+F: the keys go back to the search box, its text selected so typing replaces it.</summary>
    private void FocusSearch(PickerModel model)
    {
        model.InSettings = false;
        Search.Focus(NavigationMethod.Directional);
        Search.SelectAll();
    }

    /// <summary>The controls of the hotkey part the keys stop on (its boxes, crosses and pills), in reading order.</summary>
    private List<InputElement> HotkeyStops() =>
        HotkeyPart.GetVisualDescendants().OfType<InputElement>()
            .Where(c => c is TextBox or Button && c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled).ToList();

    /// <summary>
    /// Keys while they are in the hotkey part: a hotkey box takes its own (Enter listens, Backspace removes, Esc cancels listening); the arrows
    /// walk the boxes and pills (wrapping), Down from the last goes on to the settings; Tab or Esc go back to the list. Enter on a pill presses it.
    /// </summary>
    private bool OnHotkeyPartKey(PickerModel model, object? focused, KeyEventArgs e)
    {
        if (focused is TextBox { DataContext: HotkeyTarget target } && HotkeyCapture.TryHandle(e, target)) return true;
        var stops = HotkeyStops();
        var at = stops.IndexOf(focused as InputElement ?? Search);
        switch (e.Key)
        {
            case Key.Up or Key.Down or Key.Left or Key.Right when stops.Count > 0:
                var step = e.Key is Key.Down or Key.Right ? 1 : -1;
                if (e.Key == Key.Down && at == stops.Count - 1 && model.EnterSettings()) return true;
                stops[Services.Wrap.Index(at + step, stops.Count)].Focus(NavigationMethod.Directional);
                return true;
            case Key.Tab or Key.Escape:
                model.InSettings = false;
                Search.Focus(NavigationMethod.Directional);
                return true;
        }
        return false;
    }

    /// <summary>Turns a number into a box to type in, with the cursor in it and its text selected.</summary>
    private void BeginTyping(OptionItem item)
    {
        item.BeginTyping();
        Dispatcher.UIThread.Post(() =>
        {
            if (OptionList.ContainerFromIndex(OptionList.SelectedIndex) is not Control row) return;
            if (row.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsEffectivelyVisible) is not { } box) return;
            box.Focus(NavigationMethod.Directional);
            box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Puts the cursor in the selected text setting.</summary>
    private void EditText()
    {
        if (OptionList.ContainerFromIndex(OptionList.SelectedIndex) is not Control row) return;
        row.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsEffectivelyVisible)?.Focus(NavigationMethod.Directional);
    }

    private void OnUseClick(object? sender, RoutedEventArgs e)
    {
        if (_model is not null) UseHighlighted(_model);
    }

    private void OnRemoveFromPanelClick(object? sender, RoutedEventArgs e) => _model?.RemoveFromPanel();

    private void OnFootButtonClick(object? sender, RoutedEventArgs e) => _model?.RunFootButton();

    private void OnActionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_model is null || e.Source is Visual source && source.FindAncestorOfType<Button>() is not null) return;
        if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.DataContext is PickerRow) UseHighlighted(_model);
    }

    private void OnSectionTapped(object? sender, TappedEventArgs e)
    {
        if (_model is { IsSearching: true }) _model.Query = string.Empty; // a click on the section that was highlighted leaves the search too
        Search.Focus(NavigationMethod.Directional);
    }

    private void OnRowCommandClick(object? sender, RoutedEventArgs e)
    {
        if (_model is not null && (sender as Control)?.DataContext is PickerRow { Choice: { } choice }) _ = _model.RunCommandAsync(choice, this);
        e.Handled = true;
    }

    /// <summary>A click anywhere but in the number box being typed in takes the typed number, like Enter would.</summary>
    private void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        HotkeyCapture.CancelOnPressElsewhere(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement(), e.Source);
        if (_model?.OptionItems.FirstOrDefault(i => i.IsTyping) is not { } typing) return;
        if (e.Source is Visual source && source.FindAncestorOfType<TextBox>() is not null) return;
        typing.CommitTyping();
        FocusCurrentPane();
    }

    /// <summary>The wheel over a number or choice changes it (up = next, down = previous), like Left and Right do.</summary>
    private void OnOptionWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_model is null || (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>()?.DataContext is not OptionItem { IsStepper: true, IsTyping: false } item) return;
        _model.OptionIndex = _model.OptionItems.IndexOf(item);
        item.Step(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void OnNumberDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: OptionItem item } || _model is null) return;
        _model.OptionIndex = _model.OptionItems.IndexOf(item);
        BeginTyping(item);
        e.Handled = true;
    }

    private void OnNumberLostFocus(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is OptionItem item) item.CommitTyping();
    }

    private void OnOptionStepClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: OptionItem item, Tag: string tag } && int.TryParse(tag, out var direction)) item.Step(direction);
        e.Handled = true;
    }
}

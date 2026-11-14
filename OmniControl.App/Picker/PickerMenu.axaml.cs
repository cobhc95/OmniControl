using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OmniControl.App.Picker;

/// <summary>
/// The master picker's two-pane, searchable list, with a second page for the settings of an entry (behind its cogwheel).
/// On the first page focus never leaves the search box, so typing always searches; the arrow keys move the highlight
/// (Up/Down inside a side, Left/Right between categories, entries and the cogwheel while the box is empty).
/// On the settings page Up/Down choose a setting and Left/Right change it.
/// </summary>
public partial class PickerMenu : UserControl
{
    private PickerModel? _model;

    /// <summary>An entry was chosen, with the values of its settings.</summary>
    public event Action<PickResult>? Picked;

    /// <summary>The user asked to close the window (Esc on the first page with nothing typed).</summary>
    public event Action? CloseRequested;

    public PickerMenu()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel);
        OptionList.AddHandler(PointerWheelChangedEvent, OnOptionWheel);
        ActionList.AddHandler(TappedEvent, OnActionTapped);
        CategoryList.AddHandler(TappedEvent, OnCategoryTapped);
    }

    /// <summary>Starts a fresh pick: shows the model and puts the cursor where typing or the arrow keys belong.</summary>
    public void Show(PickerModel model)
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = model;
        model.PropertyChanged += OnModelChanged;
        DataContext = model;
        Dispatcher.UIThread.Post(() =>
        {
            FocusCurrentPage();
            CategoryList.ScrollIntoView(model.CategoryIndex);
            ActionList.ScrollIntoView(model.ActionIndex);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Lets go of the model when the window closes.</summary>
    public void Release()
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = null;
    }

    private void FocusCurrentPage()
    {
        if (_model?.IsEditingOptions == true) OptionsFocus.Focus(NavigationMethod.Directional);
        else Search.Focus(NavigationMethod.Directional);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model is null) return;
        switch (e.PropertyName)
        {
            case nameof(PickerModel.ActionIndex) when _model.ActionIndex >= 0: ActionList.ScrollIntoView(_model.ActionIndex); break;
            case nameof(PickerModel.CategoryIndex) when _model.CategoryIndex >= 0: CategoryList.ScrollIntoView(_model.CategoryIndex); break;
            case nameof(PickerModel.OptionIndex) when _model.OptionIndex >= 0: OptionList.ScrollIntoView(_model.OptionIndex); break;
            case nameof(PickerModel.IsEditingOptions): Dispatcher.UIThread.Post(FocusCurrentPage, DispatcherPriority.Loaded); break;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is not { } model) return;
        var handled = model.IsEditingOptions ? OnOptionsKey(model, e) : OnPickKey(model, e);
        if (handled) e.Handled = true;
    }

    /// <summary>Hands on the result of choosing an entry from the list (nothing when it can't be picked or its text is asked for first).</summary>
    private void Choose(PickerModel model, Mouse.PickChoice choice)
    {
        if (model.PickFor(choice) is { } pick) Picked?.Invoke(pick);
    }

    /// <summary>Keys on the first page. Returns true when the key was used.</summary>
    private bool OnPickKey(PickerModel model, KeyEventArgs e)
    {
        var typing = model.Query.Length > 0; // with text in the box, Left/Right belong to the caret

        switch (e.Key)
        {
            case Key.Up or Key.Down:
                model.Move(Services.Wrap.Direction(e.Key));
                return true;
            case Key.Left when !typing:
                if (model.InCog) model.InCog = false;
                else model.InCategories = true;
                return true;
            case Key.Right when !typing:
                if (!model.InCategories && model.CanOpenOptions && !model.InCog) model.InCog = true;
                else model.InCategories = false;
                return true;
            case Key.Enter:
                if (model.InCog) model.OpenOptions();
                else if (model.InCategories && !model.IsSearching) model.InCategories = false;
                else if (model.ActiveChoice is { } choice) Choose(model, choice);
                return true;
            case Key.Tab:
                model.OpenOptions(); // the only stop is the search box, so Tab is free to open the highlighted entry's settings
                return true;
            case Key.Escape:
                if (typing) model.Query = string.Empty;
                else CloseRequested?.Invoke();
                return true;
        }
        return false;
    }

    /// <summary>Keys on the settings page. Returns true when the key was used.</summary>
    private bool OnOptionsKey(PickerModel model, KeyEventArgs e)
    {
        var inText = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

        switch (e.Key)
        {
            case Key.Up or Key.Down:
                if (inText) OptionsFocus.Focus(NavigationMethod.Directional);
                model.MoveOption(Services.Wrap.Direction(e.Key));
                return true;
            case Key.Left or Key.Right when !inText:
                model.SelectedOption?.Step(e.Key == Key.Right ? 1 : -1);
                return true;
            case Key.Space when !inText:
                if (model.OnUseButton) UseSettings(model);
                else model.SelectedOption?.Flip();
                return true;
            case Key.Enter:
                if (model.OnUseButton) UseSettings(model);
                else if (inText) OptionsFocus.Focus(NavigationMethod.Directional); // done typing (a typed number is taken when the box loses focus)
                else if (model.SelectedOption is { IsText: true } && e.KeyModifiers != KeyModifiers.Control) EditText();
                else if (model.SelectedOption is { IsNumber: true } number && e.KeyModifiers == KeyModifiers.Control) BeginTyping(number); // Enter itself applies
                else UseSettings(model);
                return true;
            case Key.Left when e.KeyModifiers == KeyModifiers.Alt && model.CanGoBack:
                model.CloseOptions();
                return true;
            case Key.Escape:
                if (inText)
                {
                    model.SelectedOption?.CancelTyping();
                    OptionsFocus.Focus(NavigationMethod.Directional);
                }
                else model.CloseOptions();
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

    private void UseSettings(PickerModel model)
    {
        // A text being typed is taken first (a text setting commits when its box loses focus).
        OptionsFocus.Focus(NavigationMethod.Directional);
        if (model.PickFromOptions() is { } pick) Picked?.Invoke(pick);
    }

    private void OnUseClick(object? sender, RoutedEventArgs e)
    {
        if (_model is not null) UseSettings(_model);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => _model?.CloseOptions();

    private void OnActionTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>() is not null) return; // the cogwheel has its own click
        if (_model is not null && (e.Source as Control)?.FindAncestorOfType<ListBoxItem>()?.DataContext is PickerRow row)
            Choose(_model, row.Choice);
    }

    private void OnCategoryTapped(object? sender, TappedEventArgs e)
    {
        if (_model is null) return;
        _model.InCategories = true;
        Search.Focus(NavigationMethod.Directional);
    }

    private void OnRowCogClick(object? sender, RoutedEventArgs e)
    {
        if (_model is not null && (sender as Control)?.DataContext is PickerRow row) _model.OpenOptionsFor(row.Choice);
        e.Handled = true;
    }

    /// <summary>A click anywhere but in the number box being typed in takes the typed number, like Enter would.</summary>
    private void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (_model?.OptionItems.FirstOrDefault(i => i.IsTyping) is not { } typing) return;
        if (e.Source is Visual source && source.FindAncestorOfType<TextBox>() is not null) return;
        typing.CommitTyping();
        OptionsFocus.Focus(NavigationMethod.Directional);
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

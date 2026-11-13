using System.ComponentModel;
using Avalonia.Controls;

namespace OmniControl.App.Fx;

/// <summary>
/// Wiring only, for the FX window header's analysis: the pointer over the thumbnail and over the larger graph, a click (or Enter) on
/// the thumbnail, and the pop-up closing are passed to the <see cref="FxAnalysisViewModel"/>, which decides when the graph opens and
/// closes; the pop-up follows its <see cref="FxAnalysisViewModel.IsPanelOpen"/>. The window asks it to close the graph on Esc and
/// moves the keyboard to the thumbnail (its place in the window's Tab cycle).
/// </summary>
public partial class FxAnalysisBar : UserControl
{
    private FxAnalysisViewModel? _vm;

    public FxAnalysisBar()
    {
        InitializeComponent();
        Thumb.PointerEntered += (_, _) => _vm?.PointerOverThumb(true);
        Thumb.PointerExited += (_, _) => _vm?.PointerOverThumb(false);
        Thumb.Click += (_, _) => _vm?.TogglePin();
        EnlargedPanel.PointerEntered += (_, _) => _vm?.PointerOverPanel(true);
        EnlargedPanel.PointerExited += (_, _) => _vm?.PointerOverPanel(false);
        Enlarged.Closed += (_, _) => _vm?.PanelClosed();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as FxAnalysisViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        Enlarged.IsOpen = _vm?.IsPanelOpen == true;
    }

    // Set from here, not bound: a pop-up that closes itself sets IsOpen on its own, which would end a one-way binding.
    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FxAnalysisViewModel.IsPanelOpen) && _vm is { } vm && Enlarged.IsOpen != vm.IsPanelOpen)
            Enlarged.IsOpen = vm.IsPanelOpen;
    }

    /// <summary>Esc: closes the larger graph; false when it was not open.</summary>
    public bool ClosePanel() => _vm?.ClosePanel() == true;

    /// <summary>The thumbnail has the keyboard.</summary>
    public bool HasKeyboard => Thumb.IsFocused;

    public void FocusGraph() => Thumb.Focus();
}

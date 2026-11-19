using Avalonia.LogicalTree;

namespace OmniControl.App.Features.Resources.Gadget;

/// Wiring only: keeps the row told where the label box's caret and selection are and selects what the row asks (after an inserted icon, a
/// picked icon token); "+ icon" opens THE icon picker.
public partial class GadgetLabelEditor : UserControl
{
    private GadgetRowItem? _row;

    public GadgetLabelEditor()
    {
        InitializeComponent();
        LabelBox.PropertyChanged += (_, e) =>
        {
            if (_row is null) return;
            if (e.Property == TextBox.CaretIndexProperty) _row.Caret = LabelBox.CaretIndex;
            if (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
                _row.PickToken(LabelBox.SelectionStart, LabelBox.SelectionEnd);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_row is not null) _row.SelectionMoved -= OnSelectionMoved;
        _row = DataContext as GadgetRowItem;
        if (_row is not null) _row.SelectionMoved += OnSelectionMoved;
    }

    private void OnSelectionMoved(int start, int length) => Dispatcher.UIThread.Post(() =>
    {
        LabelBox.CaretIndex = start + length;
        (LabelBox.SelectionStart, LabelBox.SelectionEnd) = (start, start + length);
    }, DispatcherPriority.Loaded);

    private void OnChooseIcon(object? sender, RoutedEventArgs e)
    {
        if (_row is not { } row) return;
        Visual from = this.FindLogicalAncestorOfType<Window>() ?? (Visual)this;
        _ = Guard.RunAsync("Gadget label icon", () => row.ChooseIconAsync(from));
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        if (_row is not null) _row.SelectionMoved -= OnSelectionMoved;
        _row = null;
        base.OnDetachedFromLogicalTree(e);
    }
}

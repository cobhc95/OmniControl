using Avalonia.Controls.Shapes;

namespace OmniControl.App.Shared.Ui.Controls;

/// THE small status dot of a chip or a split pill: green for <see cref="TagTone.Good"/>, yellow for
/// Caution, red for Risk, grey for any other tone, nothing drawn while <see cref="Tone"/> is null. Look: <c>c|StatusDot</c> in Styles/RowControls.axaml.
public sealed class StatusDot : Ellipse
{
    public static readonly StyledProperty<TagTone?> ToneProperty = AvaloniaProperty.Register<StatusDot, TagTone?>(nameof(Tone));

    public StatusDot() => IsVisible = false;

    /// The dot's colour as a tag tone; null hides it.
    public TagTone? Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ToneProperty) return;
        IsVisible = Tone is not null;
        Classes.Set("good", Tone == TagTone.Good);
        Classes.Set("caution", Tone == TagTone.Caution);
        Classes.Set("risk", Tone == TagTone.Risk);
    }
}

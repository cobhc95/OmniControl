using Avalonia;
using Avalonia.Controls;
using OmniControl.App.Services;

namespace OmniControl.App.Controls;

/// <summary>Shows a <see cref="RemotePcLabel"/> (the PC an entry runs on); it is hidden when there is none, so a list binds it for every row.</summary>
public partial class RemoteSectionTag : UserControl
{
    public static readonly StyledProperty<RemotePcLabel?> LabelProperty = AvaloniaProperty.Register<RemoteSectionTag, RemotePcLabel?>(nameof(Label));

    public RemoteSectionTag()
    {
        InitializeComponent();
        IsVisible = false;
    }

    public RemotePcLabel? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != LabelProperty) return;
        DataContext = Label;
        IsVisible = Label is not null;
    }
}

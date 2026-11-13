using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace OmniControl.App.Fx;

/// <summary>
/// A popped-out effect: its name and Put back on a bar, then its editor (a plug-in's own editor in a <see cref="PluginEditorArea"/>,
/// which sizes this window to it within the screen, or OmniControl's own editor view). Closing the window puts the effect back; whoever
/// opened it releases what the editor holds.
/// </summary>
public partial class EffectEditorWindow : Window
{
    public EffectEditorWindow()
    {
        InitializeComponent();
        Bar.DoubleTapped += OnBarDoubleTapped;
    }

    /// <param name="width">Starting size (DIPs); a plug-in's editor then sizes the window to itself.</param>
    public EffectEditorWindow(string title, Control editor, bool canResize, double width, double height) : this()
    {
        Title = title;
        NameText.Text = title;
        Body.Content = editor;
        CanResize = canResize;
        Width = width;
        Height = height;
    }

    private void OnPutBack(object? sender, RoutedEventArgs e) => Close();

    private void OnBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is not null) return; // a quick double press on Put back
        Close();
    }
}

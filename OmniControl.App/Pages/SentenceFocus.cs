using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OmniControl.App.Pages;

/// <summary>
/// Keyboard focus inside a sentence of blocks (the gesture rule editor, the Automation rows): the sentence is rebuilt after every
/// change, so the keyboard is put back on the same place once it is drawn again. The crosses inside blocks are not stops.
/// </summary>
public static class SentenceFocus
{
    /// <summary>The sentence's buttons in reading order.</summary>
    public static List<Button> ButtonsOf(Control host) =>
        host.GetVisualDescendants().OfType<Button>().Where(b => !b.Classes.Contains("blockx") && b.IsEffectivelyVisible).ToList();

    /// <summary>Puts the keyboard on the button at <paramref name="index"/> (or the last one) once the sentence is drawn again.</summary>
    public static void FocusLater(Control host, int index) => Dispatcher.UIThread.Post(() =>
    {
        var buttons = ButtonsOf(host);
        if (buttons.Count > 0) buttons[Math.Clamp(index, 0, buttons.Count - 1)].Focus(NavigationMethod.Directional);
    }, DispatcherPriority.Loaded);

    /// <summary>The position of <paramref name="control"/> in the sentence, or -1.</summary>
    public static int IndexOf(Control host, object? control) => control is Button button ? ButtonsOf(host).IndexOf(button) : -1;
}

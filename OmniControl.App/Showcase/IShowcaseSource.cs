using OmniControl.App.Pages;

namespace OmniControl.App.Showcase;

/// <summary>
/// A window the export opens and captures. <paramref name="Isolated"/>: it can create or change data, so it opens only in the export run's
/// throw-away data folder. <paramref name="Finish"/>: how it is put away when it does not simply close (null: it is closed).
/// </summary>
internal sealed record ShowcaseWindow(string Name, Action Open, bool Isolated = false, Action<Avalonia.Controls.Window>? Finish = null);

/// <summary>What the export needs from the shell: its pages and windows as the shell registered them, nothing listed by the export itself.</summary>
internal interface IShowcaseSource
{
    /// <summary>Every sidebar page, in sidebar order (the main ones, then the footer's).</summary>
    IReadOnlyList<PageViewModel> ShowcasePages { get; }

    PageViewModel? ShowcaseCurrentPage { get; }

    void ShowcaseShow(PageViewModel page);

    IReadOnlyList<ShowcaseWindow> ShowcaseWindows { get; }

    /// <summary>Names of this PC's devices and monitors, to be replaced in the images.</summary>
    IEnumerable<string> ShowcasePrivateNames();

    void ShowcaseSay(string message);
}

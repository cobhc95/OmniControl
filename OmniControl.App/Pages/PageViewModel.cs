using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages;

/// <summary>
/// Base for every sidebar page. The shell only knows this type; each page's view is
/// bound to its view-model by a DataTemplate in App.axaml.
/// </summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>SVG path data (24x24 viewbox) shown next to the title in the sidebar.</summary>
    public abstract string Icon { get; }

    /// <summary>True when the page draws its own title row (to put a control beside the title); the shell then skips its title.</summary>
    public virtual bool ShowsOwnHeader => false;

    /// <summary>Extra words the sidebar search matches besides the title.</summary>
    public virtual string Keywords => string.Empty;

    /// <summary>Individual settings this page offers to global search (live, editable). Build each with <see cref="Entry"/>.</summary>
    public virtual IEnumerable<SearchEntry> SearchEntries => Enumerable.Empty<SearchEntry>();

    /// <summary>
    /// A search result for <paramref name="editor"/> on this page: its category is the page's title (what the category filter offers),
    /// followed by <paramref name="group"/> when the setting belongs to a part of the page ("Hardware", a monitor...).
    /// </summary>
    protected SearchEntry Entry(object editor, string title, string keywords, string? group = null) =>
        new() { Title = title, Category = Title, Group = group, Editor = editor, Keywords = keywords };

    /// <summary>Called when the page becomes visible; refresh live hardware state here.</summary>
    public virtual void OnShown() { }

    /// <summary>
    /// Called with true when the page comes on screen (it is the open page and the main window is showing, not minimised) and with
    /// false when it leaves (another page opens, or the window is hidden, minimised or closed). For work that must run only while the page can be seen.
    /// </summary>
    public virtual void OnOnScreenChanged(bool onScreen) { }

    /// <summary>
    /// Called on every page (open or not) whenever the main window is shown, restored, minimised, hidden or closed. For pages that keep
    /// something in step with the machine even while not on screen (registered actions of plugged devices).
    /// </summary>
    public virtual void OnWindowShownChanged(bool shown) { }

    /// <summary>Called on every page when Windows reports that the displays changed (one plugged in or removed, a resolution change).</summary>
    public virtual void OnDisplaysChanged() { }
}

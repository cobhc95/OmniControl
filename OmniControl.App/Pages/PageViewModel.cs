using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Pages.Restore;

namespace OmniControl.App.Pages;

/// <summary>
/// Base for every sidebar page. The shell only knows this type; each page's view is
/// bound to its view-model by a DataTemplate in App.axaml.
/// </summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>The sidebar group this page sits under (one of <see cref="NavGroups"/>); null: no group (a footer page).</summary>
    public virtual string? NavGroup => null;

    /// <summary>SVG path data (24x24 viewbox) shown next to the title in the sidebar.</summary>
    public abstract string Icon { get; }

    /// <summary>True while this is the page shown on the right (set by the shell; a footer button uses it for its selected look).</summary>
    public bool IsOpen { get => _isOpen; set => SetProperty(ref _isOpen, value); }
    private bool _isOpen;

    /// <summary>True when the page draws its own title row (to put a control beside the title); the shell then skips its title.</summary>
    public virtual bool ShowsOwnHeader => false;

    /// <summary>The keys every card list shares (Pages/RowEditing, GroupedCards, ValueWheel, NumberText, SliderReset), listed once in the Keyboard shortcuts window.</summary>
    public const string CardKeys =
        "Up/Down move between cards and group headers (round at the ends); Left/Right change a value, or move between the parts of a card and across a pair. "
        + "On a group header Left collapses and Right expands it. Enter opens or adjusts the control; Space flips a switch. "
        + "Ctrl+Enter or a double-click on a value types a number (Enter or Tab applies, Esc cancels); a double-click on a slider resets it.";

    /// <summary>This page's own keys beyond <see cref="CardKeys"/>, for the Keyboard shortcuts window (empty: none).</summary>
    public virtual string KeyHelp => string.Empty;

    /// <summary>What "Restore defaults" does on this page (its scope, comparison, copy, reset and undo); null: the page has nothing to restore.</summary>
    public virtual IRestorable? Restorable => null;

    /// <summary>The page's "Restore defaults" button and line, given by the shell from <see cref="Restorable"/>; null while the page has none.</summary>
    public PageRestore? Restorer { get; private set; }

    public void AttachRestorer(PageRestore restorer) => Restorer = restorer;

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

    /// <summary>"Show in OmniControl" (the tray panel) opened this page for one of its states: the page selects or brings forward what owns it (a smart device); nothing by default.</summary>
    public virtual void Reveal(string stateId) { }

    /// <summary>Called on every page when Windows reports that the displays changed (one plugged in or removed, a resolution change).</summary>
    public virtual void OnDisplaysChanged() { }
}

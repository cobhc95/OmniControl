using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.App.Shared.Ui.Pages;

/// Base for every sidebar page. The shell only knows this type; each page's view is
/// bound to its view-model by its feature module's template (<see cref="ViewTemplate"/>).
public abstract class PageViewModel : ObservableObject
{
    private readonly PageInfo _info;

    /// <param name="info">The page's sidebar facts, a static of the page the module also hands the shell (<see cref="PageInfo"/>).</param>
    protected PageViewModel(PageInfo info) => _info = info;

    public string Title => _info.Title;

    public string Icon => _info.Icon;

    /// A short fact the main window's footer shows while this page is open (the Mouse page's layer in force); empty: none.
    public string FooterStatus { get => _footerStatus; protected set => SetProperty(ref _footerStatus, value); }
    private string _footerStatus = string.Empty;

    /// True when the page draws its own title row (to put a control beside the title); the shell then skips its title.
    public virtual bool ShowsOwnHeader => false;

    /// How far the page was scrolled when it was last shown: the shell's page host builds the page's view anew at each showing and puts this back.
    public Vector Scroll { get; set; }

    /// The page's own tabs, which the shell selects when something opens the page at a tab (<c>FeatureContext.OpenPage</c>); null: no tabs.
    public virtual PageTabs? Tabs => null;

    /// The keys every card list shares (Pages/RowEditing, GroupedCards, ValueWheel, NumberText, SliderReset), listed once in the Keyboard shortcuts window.
    public const string CardKeys =
        "Up/Down move between cards and group headers (round at the ends); Left/Right change a value, or move between the parts of a card and across a pair. "
        + "On a group header Left collapses and Right expands it. Enter opens or adjusts the control; Space flips a switch. "
        + "Ctrl+Enter or a double-click on a value types a number (Enter or Tab applies, Esc cancels); a double-click on a slider resets it.";

    /// This page's own keys beyond <see cref="CardKeys"/>, for the Keyboard shortcuts window (empty: none).
    public virtual string KeyHelp => string.Empty;

    /// What "Restore defaults" does on this page (its scope, comparison, copy, reset and undo); null: the page has nothing to restore.
    public virtual IRestorable? Restorable => null;

    /// The page's "Restore defaults" button and line, given by the shell from <see cref="Restorable"/>; null while the page has none.
    public PageRestore? Restorer { get; private set; }

    public void AttachRestorer(PageRestore restorer) => Restorer = restorer;

    /// Individual settings this page offers to global search (live, editable). Build each with <see cref="Entry"/>.
    public virtual IEnumerable<SearchEntry> SearchEntries => Enumerable.Empty<SearchEntry>();

    /// A search result for <paramref name="editor"/> on this page: its category is the page's title (what the category filter offers),
    /// followed by <paramref name="group"/> when the setting belongs to a part of the page ("Hardware", a monitor...).
    protected SearchEntry Entry(object editor, string title, string keywords, string? group = null) =>
        new() { Title = title, Category = Title, Group = group, Editor = editor, Keywords = keywords };

    protected SearchEntry Entry(object editor, SearchCard card) => Entry(editor, card.Title ?? card.Label, card.Keywords, card.Group);

    /// Called when the page becomes visible; refresh live hardware state here.
    public virtual void OnShown() { }

    /// Called with true when the page comes on screen (it is the open page and the main window is showing, not minimised) and with
    /// false when it leaves (another page opens, or the window is hidden, minimised or closed). For work that must run only while the page can be seen.
    public virtual void OnOnScreenChanged(bool onScreen) { }

    /// Called on every page (open or not) whenever the main window is shown, restored, minimised, hidden or closed. For pages that keep
    /// something in step with the machine even while not on screen (registered actions of plugged devices).
    public virtual void OnWindowShownChanged(bool shown) { }

    /// "Show in OmniControl" (the tray panel) opened this page for one of its states: the page selects or brings forward what owns it (a smart device); nothing by default.
    public virtual void Reveal(string stateId) { }
}

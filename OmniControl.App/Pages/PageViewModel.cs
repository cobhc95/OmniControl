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

    /// <summary>Extra words the sidebar search matches besides the title.</summary>
    public virtual string Keywords => string.Empty;

    /// <summary>Individual settings this page offers to global search (live, editable).</summary>
    public virtual IEnumerable<SearchEntry> SearchEntries => Enumerable.Empty<SearchEntry>();

    /// <summary>Called when the page becomes visible; refresh live hardware state here.</summary>
    public virtual void OnShown() { }
}

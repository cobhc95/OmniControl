using Avalonia.Controls.Templates;
using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.App.Features;

/// One feature's wiring, listed once by the shell. <see cref="Register"/> makes the feature's services and adds its actions, states, reloads and
/// shut-down steps (modules register in the shell's start-up order); then each of the <see cref="Pages"/> registers its entries in sidebar order.
public interface IFeatureModule
{
    /// The features (<see cref="FeatureList"/> ids) whose work and page parts this module holds (the one place that says where a feature lives):
    /// it and its pages run while any of them is on, it makes only the parts of those on (<see cref="FeatureContext.Features"/>, read when it
    /// starts) and is started again when one of them changes. None: it always runs.
    IReadOnlyList<string> Features => Array.Empty<string>();

    /// True: it runs while all its <see cref="Features"/> are off too, with only its always-on parts (it reads which are on as it starts), and
    /// is still started again when one of them changes.
    bool RunsWhenOff => false;

    /// Makes the feature's services and registers what it adds through the context. Called again when the feature is switched on after being
    /// off (a live feature, <see cref="FeatureInfo.Live"/>), so it makes everything anew each time.
    void Register(FeatureContext context);

    /// The feature's sidebar pages (one that fails to be made is logged and said once).
    IReadOnlyList<FeaturePage> Pages => Array.Empty<FeaturePage>();

    /// Runs once every page has registered (for wiring that follows the pages, such as preset groups made from a page).
    void PagesMade() { }

    /// Maps the feature's view-models to their views; added to the app's templates at start-up.
    IReadOnlyList<IDataTemplate> Templates => Array.Empty<IDataTemplate>();
}

/// A sidebar page a module offers: what the shell shows of it without making it, and how its view-model is made: the first time the page is
/// opened, searched or asked to reveal a state, then kept for the run.
/// <param name="Words">The words of every search result the page offers (<see cref="SearchCard.Words"/>, <see cref="SearchEntry.WordsOf"/>),
/// read from the card definitions and the owning services, never from the page, so search makes only the pages with a match.</param>
public sealed record FeaturePage(PageInfo Info, Func<PageViewModel> Make, Func<IEnumerable<string>> Words)
{
    /// What the page puts in the master lists, through the module's own services, at start-up and at its place in the sidebar order (so the
    /// lists keep the order they had when every page was made at start-up).
    public Action? Registers { get; init; }

    /// The page's "Restore defaults" (its <see cref="PageInfo.RestoreKey"/>), made over the module's services right after <see cref="Registers"/>:
    /// the page's pill, its action and Restore all read and run it without making the page.
    public Func<IRestorable>? Restore { get; init; }

    /// The word source of a page that offers no search results.
    public static IEnumerable<string> NoResults() => Array.Empty<string>();
}

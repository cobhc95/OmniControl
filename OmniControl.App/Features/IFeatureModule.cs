using Avalonia.Controls.Templates;

namespace OmniControl.App.Features;

/// One feature's wiring, listed once by the shell. <see cref="Register"/> makes the feature's services and adds its actions, states, reloads and
/// shut-down steps (modules register in the shell's start-up order); <see cref="Pages"/> are made after every module registered, in sidebar order.
public interface IFeatureModule
{
    void Register(FeatureContext context);

    /// The feature's sidebar pages, each made on its own (one that fails is left out).
    IReadOnlyList<FeaturePage> Pages => Array.Empty<FeaturePage>();

    /// Runs once every page is made (for wiring that follows the pages, such as preset groups made from a page).
    void PagesMade() { }

    /// Maps the feature's view-models to their views; added to the app's templates at start-up.
    IReadOnlyList<IDataTemplate> Templates => Array.Empty<IDataTemplate>();
}

/// A sidebar page a module offers: its name (for the start-up log) and how it is made.
public sealed record FeaturePage(string Name, Func<PageViewModel> Make);

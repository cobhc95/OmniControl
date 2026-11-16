using OmniControl.App.Apps;
using OmniControl.App.Services;
using Xunit;

namespace OmniControl.Tests;

public class AppCatalogueTests
{
    [Fact]
    public void Ids_and_titles_are_unique() =>
        Assert.True(AppCatalogue.All.Select(a => a.Id).Distinct().Count() == AppCatalogue.All.Count
            && AppCatalogue.All.Select(a => a.Title).Distinct().Count() == AppCatalogue.All.Count);

    [Fact]
    public void Every_app_says_how_to_find_it() =>
        Assert.All(AppCatalogue.All, a => Assert.True(
            a.Locator.AppPaths.Count + a.Locator.KnownPaths.Count + a.Locator.StartMenuNames.Count > 0
            || a.Locator.StorePackage is not null || a.Locator.Direct is not null, a.Id));

    [Fact]
    public void Action_id_names_its_app() => Assert.Equal(AppCatalogue.All[0], AppCatalogue.OfAction(AppCatalogue.All[0].ActionId));

    [Fact]
    public void Launch_limit_refuses_a_burst()
    {
        var limiter = new LaunchLimiter();
        var allowed = Enumerable.Range(0, Timing.LaunchBurstMax + 2).Count(_ => limiter.TryAcquire());
        Assert.Equal(Timing.LaunchBurstMax, allowed);
    }
}

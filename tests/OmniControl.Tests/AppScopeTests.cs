using OmniControl.App.AppLists;
using Xunit;

namespace OmniControl.Tests;

public class AppScopeTests
{
    private static AppListStore NewStore() => new(new AppListSettings(), () => { });

    [Theory]
    [InlineData(ScopeMode.All, true, true)]
    [InlineData(ScopeMode.All, false, true)]
    [InlineData(ScopeMode.Only, true, true)]
    [InlineData(ScopeMode.Only, false, false)]
    [InlineData(ScopeMode.Except, true, false)]
    [InlineData(ScopeMode.Except, false, true)]
    public void Scope_allows_by_mode(ScopeMode mode, bool listed, bool allowed) =>
        Assert.Equal(allowed, new AppScopeSetting(mode, "x").Allows(listed));

    [Fact]
    public void A_mode_without_a_list_limits_nothing() => Assert.False(new AppScopeSetting(ScopeMode.Only).IsLimited);

    [Fact]
    public void Lists_get_unique_names_and_apps_are_added_once_by_identity()
    {
        var store = NewStore();
        var first = store.Create("Games");
        var second = store.Create("games");
        Assert.NotEqual(first.Name, second.Name);
        Assert.True(store.Add(first, @"C:\Games\Steam.exe", "Steam"));
        Assert.False(store.Add(first, "steam", "Steam again"));
        Assert.Contains("steam", store.Members(first.Id)!);
    }

    [Fact]
    public void Deleting_a_list_puts_its_users_back_to_all_apps()
    {
        var store = NewStore();
        var list = store.Create("Games");
        AppScopeSetting? group = new(ScopeMode.Except, list.Id);
        var model = new AppScopeModel(store, "Test", () => group, scope => group = scope, () => { });
        Assert.Equal("Apps: all except Games", model.Text);
        Assert.Single(store.UsersOf(list.Id));
        store.Delete(list);
        Assert.Null(group);
        Assert.Equal("Apps: all", model.Text);
        Assert.Null(store.Members(list.Id));
    }

    [Fact]
    public void A_card_shows_a_tag_only_when_it_differs_from_its_group()
    {
        var store = NewStore();
        var games = store.Create("Games");
        AppScopeSetting? groupScope = new(ScopeMode.Only, games.Id);
        AppScopeSetting? cardScope = null;
        var group = new AppScopeModel(store, "Group", () => groupScope, scope => groupScope = scope, () => { });
        var card = new AppScopeModel(store, "Card", () => cardScope, scope => cardScope = scope, () => { }, group);
        Assert.False(card.ShowsTag);
        Assert.Equal(group.Text, card.Text);
        cardScope = AppScopeSetting.All;
        Assert.True(card.ShowsTag);
        cardScope = groupScope;
        Assert.False(card.ShowsTag);
    }
}

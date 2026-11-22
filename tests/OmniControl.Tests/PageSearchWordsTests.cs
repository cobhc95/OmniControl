using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Models;
using System.Text.RegularExpressions;
using OmniControl.App.Shared.Ui.Controls;
using OmniControl.App.Shared.Ui.Pages;

namespace OmniControl.Tests;

/// Nothing a page shows may be missed by search. Search makes only the pages whose words match (FeaturePage.Words), so every page that offers
/// results declares them once, through its SearchBuilder: its SearchCards are both its results and its words, and a card's label, choices, menu,
/// tooltip and muted line are searchable. Pages need their services and Windows, so they are not made here: each page's word source is called
/// with no services (its static cards), the cards the page makes its items from are checked on their items, and the registrations and the
/// page's own files are read from the source.
public class PageSearchWordsTests
{
    // Pages that still list their results by hand (an override of SearchEntries). The coverage job moves each onto the builder and deletes its line.
    private static readonly Dictionary<string, string> NotOnTheBuilderYet = new()
    {
        ["MousePageViewModel"] = "cards are built twice (static words, instance entries)",
        ["ResourcesPageViewModel"] = "readings and the gadget tab are listed by hand",
        ["TrafficPageViewModel"] = "only the packet filter card is listed",
        ["VolumePageViewModel"] = "cables and mixer rows are listed by hand",
    };

    // Pages that offer no search results at all (FeaturePage.NoResults): found only by their keywords and their actions' categories.
    private static readonly Dictionary<string, string> NoResultsAllowed = new()
    {
        ["DevicesPageViewModel"] = "its cards come from the devices; the coverage job gives it results",
        ["OtherPcsPageViewModel"] = "its cards come from the paired PCs; the coverage job gives it results",
    };

    private static readonly Type[] AllPages = typeof(PageViewModel).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(PageViewModel)) && !t.IsAbstract).ToArray();

    private static bool Overrides(Type page, string property) =>
        page.GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetMethod!.DeclaringType != typeof(PageViewModel);

    private static readonly Type[] PagesWithResults = AllPages
        .Where(t => Overrides(t, nameof(PageViewModel.SearchEntries)) || Overrides(t, "SearchCards")).ToArray();

    private static MethodInfo? WordsOf(Type page) => page.GetMethod("Words", BindingFlags.Public | BindingFlags.Static);

    private static IReadOnlyList<string> WordsWithoutServices(Type page)
    {
        var words = WordsOf(page)!;
        var args = words.GetParameters().Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        return ((IEnumerable<string>)words.Invoke(null, args)!).ToList();
    }

    private static string AppRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !Directory.Exists(Path.Combine(root, "OmniControl.App"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return Path.Combine(root, "OmniControl.App");
    }

    private static string AllSource() =>
        string.Join("\n", Directory.EnumerateFiles(AppRoot(), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

    /// The page's own files: its class and the partials named after it.
    private static string PageSource(Type page) => string.Join("\n", Directory.EnumerateFiles(AppRoot(), "*.cs", SearchOption.AllDirectories)
        .Where(f => Path.GetFileName(f).StartsWith(page.Name + ".", StringComparison.Ordinal)).Select(File.ReadAllText));

    // Every card a page shows is one of these; each has a constructor that takes its SearchCard.
    private static readonly Type[] CardTypes = typeof(IEditorItem).Assembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && typeof(IEditorItem).IsAssignableFrom(t) && t.Namespace == typeof(IEditorItem).Namespace)
        .ToArray();

    private static bool HasCardConstructor(Type t) => t.GetConstructors().Any(c => c.GetParameters().FirstOrDefault()?.ParameterType == typeof(SearchCard));

    private static bool HasLabelConstructor(Type t) => t.GetConstructors().Any(c => c.GetParameters().FirstOrDefault() is { ParameterType: var type, Name: "label" } && type == typeof(string));

    [Fact]
    public void EveryPageWithResultsHasAWordSource()
    {
        Assert.NotEmpty(PagesWithResults);
        foreach (var page in PagesWithResults)
        {
            var words = WordsOf(page);
            Assert.True(words is not null && typeof(IEnumerable<string>).IsAssignableFrom(words.ReturnType), $"{page.Name} has no public static Words");
        }
    }

    [Fact]
    public void EveryWordSourceIsRegisteredWithItsPage()
    {
        var source = AllSource();
        foreach (var page in PagesWithResults) Assert.True(source.Contains($"{page.Name}.Words"), $"{page.Name}'s Words is not given to its page registration");
    }

    [Fact]
    public void EveryPageIsOnTheBuilder_OrNamedInAnAllowList()
    {
        foreach (var page in AllPages)
        {
            var builder = Overrides(page, "SearchCards");
            var byHand = Overrides(page, nameof(PageViewModel.SearchEntries));
            Assert.False(builder && byHand, $"{page.Name} overrides both SearchCards and SearchEntries; use the builder only");
            if (NotOnTheBuilderYet.ContainsKey(page.Name)) Assert.True(byHand && !builder, $"{page.Name} is on the builder now: delete its line of NotOnTheBuilderYet");
            else if (NoResultsAllowed.ContainsKey(page.Name)) Assert.False(builder || byHand, $"{page.Name} has results now: delete its line of NoResultsAllowed");
            else Assert.True(builder, $"{page.Name} must define its search through SearchCards (the SearchBuilder); a page without results goes in NoResultsAllowed with its reason");
        }
        foreach (var name in NotOnTheBuilderYet.Keys.Concat(NoResultsAllowed.Keys)) Assert.Contains(AllPages, p => p.Name == name);
    }

    [Fact]
    public void OnlyTheAllowedPagesRegisterNoResults()
    {
        var source = AllSource();
        var registered = Regex.Matches(source, @"new FeaturePage\((\w+)\.Info,(?:(?!new FeaturePage\().)*?FeaturePage\.NoResults", RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value).ToHashSet();
        foreach (var name in registered) Assert.True(NoResultsAllowed.ContainsKey(name), $"{name} registers FeaturePage.NoResults without an entry in NoResultsAllowed");
        foreach (var name in NoResultsAllowed.Keys) Assert.Contains(name, registered);
    }

    [Fact]
    public void EveryCardTypeHasACardConstructor()
    {
        Assert.NotEmpty(CardTypes);
        foreach (var type in CardTypes.Where(HasLabelConstructor))
            Assert.True(HasCardConstructor(type), $"{type.Name} has a (string label, ...) constructor but none that takes a SearchCard");
    }

    [Fact]
    public void APageOnTheBuilderMakesEveryCardFromItsSearchCard()
    {
        var types = CardTypes.Where(HasCardConstructor).Select(t => t.Name).ToList();
        Assert.NotEmpty(types);
        foreach (var page in PagesWithResults.Where(p => !NotOnTheBuilderYet.ContainsKey(p.Name)))
        {
            var source = PageSource(page);
            Assert.NotEmpty(source);
            foreach (var type in types)
                foreach (var first in FirstArguments(source, type))
                    Assert.True(!first.StartsWith('"') && !first.StartsWith("$\"") && first.Contains("Card"),
                        $"{page.Name} makes a {type} from \"{first}\"; pass its SearchCard so its label, choices, tooltip and menu are searchable");
        }
    }

    // The first argument of each "new <type>(" in the source.
    private static IEnumerable<string> FirstArguments(string source, string type)
    {
        var marker = $"new {type}(";
        for (var at = source.IndexOf(marker, StringComparison.Ordinal); at >= 0; at = source.IndexOf(marker, at + marker.Length, StringComparison.Ordinal))
        {
            var start = at + marker.Length;
            var depth = 0;
            var i = start;
            for (; i < source.Length; i++)
            {
                var c = source[i];
                if (c == '"')
                {
                    for (i++; i < source.Length && source[i] != '"'; i++) if (source[i] == '\\') i++;
                }
                else if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}')
                {
                    if (depth == 0) break;
                    depth--;
                }
                else if (c == ',' && depth == 0) break;
            }
            yield return source[start..i].Trim();
        }
    }

    [Fact]
    public void EveryStaticCardIsInItsPagesWords()
    {
        foreach (var page in PagesWithResults)
        {
            var words = WordsWithoutServices(page);
            var cards = page.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(f => f.FieldType == typeof(SearchCard)).Select(f => (SearchCard)f.GetValue(null)!).ToList();
            foreach (var card in cards) Assert.Contains(card.Words, words);
            if (cards.Count > 0) Assert.NotEmpty(words);
        }
    }

    [Fact]
    public void ACardsWordsHoldItsChoicesMenuTooltipAndNote()
    {
        var card = new SearchCard("When closing", "close", "Safety", "Start and close")
        {
            Choices = new[] { "Ask each time", "Minimise to tray" },
            Menu = new[] { "Manage places…" },
            Tip = "How the X button acts",
            Note = "Remembered",
        };
        var words = TextMatch.Haystack(new[] { Heading.Join("App settings", card.Words) });

        foreach (var text in new[] { "ask each time", "minimise to tray", "manage places", "x button acts", "remembered", "safety", "when closing", "start and close" })
            Assert.True(TextMatch.AllIn(TextMatch.Words(text), words), text);
    }

    [Fact]
    public void ACardsWordsFindEveryWordItsResultIsFoundBy()
    {
        var card = new SearchCard("Check hardware every", "hardware periodic minutes", "Safety", "Start and close") { Choices = new[] { "Off", "1 minute" }, Tip = "Looks again" };
        var entry = SearchEntry.Of("App settings", card, new Labelled(card.Label));
        var pageWords = TextMatch.Haystack(new[] { Heading.Join("App settings", card.Words) });
        foreach (var word in TextMatch.Words(string.Join(' ', entry.SearchFields)))
            Assert.True(TextMatch.AllIn(new[] { word }, pageWords), word);
    }

    [Fact]
    public void TheBuilderGivesWordsForEveryCardAndResultsOnlyForThoseWithAnEditor()
    {
        var shown = new SearchCard("Shown", "k");
        var absent = new SearchCard("Absent", "k");
        var builder = new SearchBuilder().Add(shown, new Labelled("Shown")).Add(absent);

        Assert.Equal(2, builder.Words.Count());
        Assert.Equal(new[] { "Shown" }, builder.Entries("Page").Select(e => e.Title));
    }

    [Fact]
    public void EveryItemMadeFromACardShowsAndSearchesWhatTheCardSays()
    {
        var card = new SearchCard("Card", "k") { Choices = new[] { "One", "Two" }, Tip = "A tip", Note = "A note" };
        void Has(IEditorItem item, params string[] texts)
        {
            Assert.Equal("Card", item.Label);
            foreach (var text in texts) Assert.Contains(text, item.SearchTexts);
        }

        Has(new ToggleItem(card, false, _ => { }), "A tip", "A note");
        Has(new ChoiceItem(card, "One", _ => { }), "One", "Two", "A tip");
        Has(new GalleryItem(card, 0, _ => { }), "One", "Two", "A tip");
        Has(new ButtonItem(card, "Go", () => { }), "A note");
        Has(new SliderItem(card, 0, _ => OperationResult.Ok(), _ => { }), "A note");
        Has(new MenuChoiceItem(card, () => "Now", () => new[] { new MenuRowItem("Row", string.Empty, false, "Row tip", new RelayCommand(() => { })) }), "A tip", "Now", "Row", "Row tip");
        Has(new FactItem(card), "A note");
        Has(new TextItem(card, string.Empty, _ => { }), "A note");
        Has(new FileItem(card, FileKind.OfFolder("Choose"), _ => { }), "A note");
        Has(new ChoiceChipsItem(card, Array.Empty<ChipItem>()), "A note");
        Assert.Equal("Card", new AnchorItem(card, 0, _ => { }).Label);
    }

    private sealed record Labelled(string Label) : IEditorItem;
}

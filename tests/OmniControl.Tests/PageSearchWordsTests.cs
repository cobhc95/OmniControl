using System.Reflection;
using OmniControl.App.Shared.Ui.Pages;

namespace OmniControl.Tests;

/// Search makes only the pages whose words match (FeaturePage.Words), so every page that offers results must declare its words without
/// being made, and those words must hold every result's own. Pages need their services and Windows, so they are not made here: each page's
/// word source is called with no services (its static cards), and the registrations are read from the source.
public class PageSearchWordsTests
{
    private static readonly Type[] PagesWithResults = typeof(PageViewModel).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(PageViewModel)) && !t.IsAbstract
            && t.GetProperty(nameof(PageViewModel.SearchEntries))!.GetMethod!.DeclaringType != typeof(PageViewModel))
        .ToArray();

    private static MethodInfo? WordsOf(Type page) => page.GetMethod("Words", BindingFlags.Public | BindingFlags.Static);

    private static IReadOnlyList<string> WordsWithoutServices(Type page)
    {
        var words = WordsOf(page)!;
        var args = words.GetParameters().Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        return ((IEnumerable<string>)words.Invoke(null, args)!).ToList();
    }

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
        var root = AppContext.BaseDirectory;
        while (root is not null && !Directory.Exists(Path.Combine(root, "OmniControl.App"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var source = string.Join("\n", Directory.EnumerateFiles(Path.Combine(root, "OmniControl.App"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        foreach (var page in PagesWithResults) Assert.True(source.Contains($"{page.Name}.Words"), $"{page.Name}'s Words is not given to its page registration");
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
    public void ACardsWordsFindEveryWordItsResultIsFoundBy()
    {
        var card = new SearchCard("Check hardware every", "hardware periodic minutes", "Safety", "Start and close");
        var entry = new SearchEntry { Title = card.Title!, Category = "App settings", Group = card.Group, Editor = new Labelled(card.Label), Keywords = card.Keywords };
        var pageWords = TextMatch.Haystack(new[] { Heading.Join("App settings", card.Words) });
        foreach (var word in TextMatch.Words(string.Join(' ', entry.SearchFields)))
            Assert.True(TextMatch.AllIn(new[] { word }, pageWords), word);
    }

    private sealed record Labelled(string Label) : IEditorItem;
}

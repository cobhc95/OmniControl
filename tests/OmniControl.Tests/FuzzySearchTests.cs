using OmniControl.App.Shared.Ui.Pages;

namespace OmniControl.Tests;

/// The typo-tolerant tier of search: it counts edits (a swap is one), scales the allowance with the word's length, ranks the nearest first, caps
/// the list and highlights the part of a text the query matches.
public class FuzzySearchTests
{
    private sealed record Item(string Name);

    private static SearchIndex<Item> IndexOf(params string[] names) => new(names.Select(n => new Item(n)), i => new[] { i.Name });

    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("brightnes", "brightness", 1)]
    [InlineData("volmue", "volume", 1)]
    [InlineData("same", "same", 0)]
    public void DistanceCountsInsertsReplacesAndSwaps(string a, string b, int edits) => Assert.Equal(edits, FuzzyMatch.Distance(a, b));

    [Fact]
    public void ALongerWordAllowsMoreEdits_AShortOneNone()
    {
        Assert.Equal(0, FuzzyMatch.Allowed(3));
        Assert.Equal(1, FuzzyMatch.Allowed(5));
        Assert.Equal(2, FuzzyMatch.Allowed(9));
        Assert.Equal(-1, FuzzyMatch.Between("vlm", "volume"));
        Assert.Equal(1, FuzzyMatch.Between("minimii", "minimise"));
        Assert.Equal(0, FuzzyMatch.Between("minim", "minimise"));
    }

    [Fact]
    public void SimilarFindsATypoExactDoesNot_NearestFirst_ThenListOrder()
    {
        var index = IndexOf("Minimise to tray", "Brightness", "Minimize everything", "Quit");

        Assert.Empty(index.Match("minimii"));
        Assert.Equal(new[] { "Minimise to tray" }, index.Similar("minimii").Select(i => i.Name).Take(1));
        Assert.DoesNotContain("Quit", index.Similar("minimii").Select(i => i.Name));
        Assert.Equal(new[] { "Brightness" }, index.Similar("brigthness").Select(i => i.Name));
    }

    [Fact]
    public void EveryQueryWordMustBeNearSomeWord()
    {
        var index = IndexOf("Minimise to tray", "Minimise windows");

        Assert.Equal(new[] { "Minimise to tray" }, index.Similar("minimsie tray").Select(i => i.Name));
    }

    [Fact]
    public void SimilarResultsAreCapped()
    {
        var index = IndexOf(Enumerable.Range(0, 100).Select(i => $"Brightness {i}").ToArray());

        Assert.Equal(FuzzyMatch.Cap, index.Similar("brigthness").Count);
    }

    [Fact]
    public void ACancelledSimilarSearchStops()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => IndexOf("A").Similar("anything", cancel.Token));
    }

    [Fact]
    public void HitsMarkWhereAWordIs_OrWhatANearMissSharesWithAWord()
    {
        Assert.Equal(new[] { (0, 5) }, FuzzyMatch.Hits("Minimise to tray", new[] { "minim" }));
        Assert.Equal(new[] { (0, 6) }, FuzzyMatch.Hits("Minimised", new[] { "minimii" }));
        Assert.Equal(new[] { (0, 3), (9, 3) }, FuzzyMatch.Hits("Tray and tray", new[] { "tra" }));
        Assert.Empty(FuzzyMatch.Hits("Brightness", new[] { "xyz" }));
    }

    [Fact]
    public void AnEntryNamesTheDetailThatHoldsTheMatchOnlyWhenTheTitleDoesNot()
    {
        var card = new SearchCard("When closing", "close", Title: "Start and close") { Choices = new[] { "Ask each time", "Minimise to tray", "Quit" } };
        var entry = SearchEntry.Of("App settings", card, new Labelled("When closing"));

        Assert.Equal("Minimise to tray", entry.FoundIn(new[] { "minimise" }));
        Assert.Null(entry.FoundIn(new[] { "closing" }));
        Assert.Contains("Quit", entry.SearchFields);
    }

    private sealed record Labelled(string Label) : IEditorItem;
}

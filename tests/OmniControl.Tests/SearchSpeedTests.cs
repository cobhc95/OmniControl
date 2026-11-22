using System.Diagnostics;
using Xunit.Abstractions;

namespace OmniControl.Tests;

/// The typo-tolerant pass runs at the typing pause on every key, so it stays cheap on a list the size of every page's cards, and the category
/// filter acts before the cap.
public class SearchSpeedTests(ITestOutputHelper output)
{
    private sealed record Entry(string Category, string Text);

    // An index as big as the app's: a card is a few dozen words, drawn from a vocabulary of a few thousand (cards share many of them).
    private static SearchIndex<Entry> BigIndex(int cards, int vocabulary, int wordsPerCard)
    {
        var random = new Random(7);
        var words = Enumerable.Range(0, vocabulary).Select(i => $"{(char)('a' + i % 26)}{(char)('a' + i / 26 % 26)}{(char)('a' + i / 676 % 26)}word{i % 97}").ToArray();
        var entries = Enumerable.Range(0, cards)
            .Select(i => new Entry($"Page {i % 20}", string.Join(' ', Enumerable.Range(0, wordsPerCard).Select(_ => words[random.Next(vocabulary)]))))
            .ToList();
        return new SearchIndex<Entry>(entries, e => new[] { e.Category, e.Text });
    }

    [Fact]
    public void ATypoSearchOverTheWholeAppStaysCheap()
    {
        var index = BigIndex(cards: 3000, vocabulary: 4000, wordsPerCard: 40);
        index.Similar("brigthness"); // the first one makes the word lists

        var clock = Stopwatch.StartNew();
        const int runs = 20;
        for (var i = 0; i < runs; i++) index.Similar("bkwrod5 volmue");
        var each = clock.Elapsed.TotalMilliseconds / runs;
        output.WriteLine($"Similar over 3000 cards of 40 words: {each:0.0} ms per search");

        Assert.True(each < 50, $"{each:0.0} ms per typo search is too slow for a key press");
    }

    [Fact]
    public void TheCategoryFilterActsBeforeTheCap()
    {
        var entries = Enumerable.Range(0, 100).Select(i => new Entry(i < 60 ? "Displays" : "Volume", $"Brightness {i}")).ToList();
        var index = new SearchIndex<Entry>(entries, e => new[] { e.Text });

        var volume = index.Similar("brigthness", accept: e => e.Category == "Volume");

        Assert.Equal(FuzzyMatch.Cap, volume.Count);
        Assert.All(volume, e => Assert.Equal("Volume", e.Category));
        Assert.Empty(index.Match("brightness", accept: e => e.Category == "Mouse"));
    }
}

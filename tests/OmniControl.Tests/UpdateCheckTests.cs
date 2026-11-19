using OmniControl.App.Shared.Services.Updates;

namespace OmniControl.Tests;

/// The update check's pure parts: version order, reading GitHub's release list, and when the daily check is due.
public class UpdateCheckTests
{
    private static ReleaseVersion V(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var v), text);
        return v;
    }

    [Theory]
    [InlineData("0.1.9", "0.2.0")]
    [InlineData("0.1.5", "0.1.6")]
    [InlineData("0.9.0", "0.10.0")]
    [InlineData("0.2.0-beta.1", "0.2.0")]
    [InlineData("0.2.0-beta.2", "0.2.0-beta.10")]
    [InlineData("0.2.0-alpha", "0.2.0-beta")]
    [InlineData("0.2.0-1", "0.2.0-alpha")]
    [InlineData("0.1.9", "0.2.0-rc.1")]
    public void OlderComesFirst(string older, string newer)
    {
        Assert.True(V(older).CompareTo(V(newer)) < 0);
        Assert.True(V(newer).IsNewerThan(V(older)));
        Assert.False(V(older).IsNewerThan(V(newer)));
    }

    [Theory]
    [InlineData("v0.1.6", "0.1.6")]
    [InlineData("0.1.6+abc123", "0.1.6")]
    [InlineData("0.2", "0.2.0")]
    public void SpellingsOfOneVersionAreEqual(string a, string b) => Assert.Equal(0, V(a).CompareTo(V(b)));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x.3")]
    public void TextThatIsNotAVersionIsRefused(string text) => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Fact]
    public void NewestReleaseWinsAndDraftsAreSkipped()
    {
        const string json = """
            [
              {"tag_name":"v0.1.4","html_url":"https://github.com/o/r/releases/tag/v0.1.4","published_at":"2026-11-10T10:00:00Z","prerelease":true,"draft":false},
              {"tag_name":"v0.1.6","html_url":"https://github.com/o/r/releases/tag/v0.1.6","published_at":"2026-11-17T09:00:00Z","prerelease":true,"draft":false},
              {"tag_name":"v0.9.0","html_url":"https://github.com/o/r/releases/tag/v0.9.0","prerelease":false,"draft":true},
              {"tag_name":"nightly","html_url":"https://github.com/o/r/releases/tag/nightly","prerelease":true,"draft":false}
            ]
            """;
        var newest = UpdateFeed.Newest(json);
        Assert.NotNull(newest);
        Assert.Equal("0.1.6", newest!.Version);
        Assert.True(newest.Prerelease);
        Assert.Equal("https://github.com/o/r/releases/tag/v0.1.6", newest.Url);
        Assert.NotNull(newest.Published);
    }

    [Fact]
    public void ALinkOutsideGitHubOpensTheReleasesPageInstead()
    {
        var newest = UpdateFeed.Newest("""[{"tag_name":"v1.0.0","html_url":"https://example.com/x"}]""");
        Assert.Equal(AppInfo.ReleasesPageUrl, newest!.Url);
    }

    [Fact]
    public void AnEmptyListHasNoRelease() => Assert.Null(UpdateFeed.Newest("[]"));

    [Fact]
    public void AnAnswerThatIsNotAListThrows() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => UpdateFeed.Newest("""{"message":"rate limited"}"""));

    [Fact]
    public void TheRepositoryIsWrittenOnce() => Assert.Contains(AppInfo.Repository, AppInfo.ReleasesApiUrl);

    private static readonly DateTime Noon = new(2026, 11, 17, 12, 0, 0);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    [Fact]
    public void NeverCheckedIsDueWhenOn() => Assert.True(UpdateSchedule.IsDue(true, null, Noon, Day));

    [Fact]
    public void SwitchedOffIsNeverDue() => Assert.False(UpdateSchedule.IsDue(false, null, Noon, Day));

    [Fact]
    public void ACheckWithinTheDayIsNotRepeated() => Assert.False(UpdateSchedule.IsDue(true, Noon.AddHours(-23), Noon, Day));

    [Fact]
    public void ADayLaterIsDue()
    {
        Assert.True(UpdateSchedule.IsDue(true, Noon.AddHours(-24), Noon, Day));
        Assert.True(UpdateSchedule.IsDue(true, Noon.AddDays(-3), Noon, Day));
    }

    [Fact]
    public void AClockSetBackIsDue() => Assert.True(UpdateSchedule.IsDue(true, Noon.AddHours(5), Noon, Day));

    [Fact]
    public async Task ACheckKeepsWhatItFoundAndAFailureIsCalm()
    {
        var state = new UpdateSettings();
        var saves = 0;
        var answer = """[{"tag_name":"v0.2.0","html_url":"https://github.com/o/r/releases/tag/v0.2.0","prerelease":true}]""";
        var fail = false;
        using var service = new UpdateService(state, () => saves++, "0.1.9", _ => fail ? throw new HttpRequestException("offline") : Task.FromResult(answer), () => Noon);

        await service.CheckAsync();
        Assert.Equal("0.2.0", service.Available?.Version);
        Assert.False(service.LastFailed);
        Assert.Equal(Noon, service.LastCheck);

        fail = true;
        await service.CheckAsync();
        Assert.True(service.LastFailed);
        Assert.Equal("0.2.0", service.Available?.Version); // the earlier answer stays
        Assert.True(saves >= 2);
    }

    [Fact]
    public async Task NoUpdateWhenThisVersionIsTheNewest()
    {
        var answer = """[{"tag_name":"v0.1.9","html_url":"https://github.com/o/r/releases/tag/v0.1.9"}]""";
        using var service = new UpdateService(new UpdateSettings(), () => { }, "0.1.9", _ => Task.FromResult(answer), () => Noon);
        await service.CheckAsync();
        Assert.Null(service.Available);
    }
}

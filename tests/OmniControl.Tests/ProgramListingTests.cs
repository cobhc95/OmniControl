using OmniControl.App.Audio;
using OmniControl.Core.Engine;

namespace OmniControl.Tests;

/// <summary>Which programs the Mixer lists (Show closed) and which the "Add program" menu offers; the installed-program clean-up (dedupe, setup tools).</summary>
public class ProgramListingTests
{
    private const string Windows = @"C:\Windows";

    private static AppAudioEntry Entry(string path, string name, bool added = false, int volume = 100) =>
        new() { Key = AppAudioEntry.KeyFor(path), ExePath = path, Name = name, Added = added, Volume = volume };

    [Theory]
    [InlineData(true, false, false, true)]   // open: always a row, with nothing set and with Show closed off
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, true)]    // closed but remembered, Show closed on
    [InlineData(false, true, false, false)]  // closed but remembered, Show closed off: hidden
    [InlineData(false, false, true, false)]  // closed with nothing worth remembering: never a row
    [InlineData(false, false, false, false)]
    public void IsListed_OpenOrRememberedAndShown(bool open, bool keepsRow, bool showClosed, bool expected) =>
        Assert.Equal(expected, ProgramListing.IsListed(open, keepsRow, showClosed));

    [Fact]
    public void KeepsRow_AddedOrCustomised()
    {
        Assert.False(Entry(@"c:\a\a.exe", "A").KeepsRow);
        Assert.True(Entry(@"c:\a\a.exe", "A", added: true).KeepsRow);
        Assert.True(Entry(@"c:\a\a.exe", "A", volume: 40).KeepsRow);
    }

    [Fact]
    public void Choices_RecentlyUsedFirst_ThenInstalled_EachAlphabetical()
    {
        var remembered = new[] { Entry(@"c:\x\zed.exe", "Zed"), Entry(@"c:\x\abe.exe", "Abe") };
        var installed = new[] { new FoundProgram("Mid", @"c:\x\mid.exe"), new FoundProgram("Bob", @"c:\x\bob.exe") };
        var choices = ProgramListing.Choices(remembered, installed, string.Empty);
        Assert.Equal(new[] { "Abe", "Zed", "Bob", "Mid" }, choices.Select(c => c.Name));
        Assert.Equal(new[] { ProgramListing.RecentlyUsedWord, ProgramListing.RecentlyUsedWord, ProgramListing.InstalledWord, ProgramListing.InstalledWord }, choices.Select(c => c.Source));
    }

    [Fact]
    public void Choices_LeaveOutProgramsThatAlreadyHaveARowAndDuplicates()
    {
        var remembered = new[] { Entry(@"c:\x\row.exe", "Row", added: true), Entry(@"c:\x\seen.exe", "Seen") };
        var installed = new[]
        {
            new FoundProgram("Row again", @"C:\X\ROW.exe"),     // already a row (same path, other case)
            new FoundProgram("Seen again", @"c:\x\seen.exe"),   // already offered as recently used
            new FoundProgram("New", @"c:\x\new.exe"),
        };
        var choices = ProgramListing.Choices(remembered, installed, string.Empty);
        Assert.Equal(new[] { "Seen", "New" }, choices.Select(c => c.Name));
    }

    [Fact]
    public void Choices_LeaveOutIgnoredSetupTools()
    {
        var remembered = new[] { Entry(@"c:\program files\equalizerapo\editor.exe", "Editor") };
        var installed = new[] { new FoundProgram("VB-CABLE", @"c:\dl\vbcable_setup_x64.exe"), new FoundProgram("Fine", @"c:\x\fine.exe") };
        Assert.Equal(new[] { "Fine" }, ProgramListing.Choices(remembered, installed, string.Empty).Select(c => c.Name));
    }

    [Fact]
    public void Choices_SearchMatchesNameOrFileNameIgnoringCase()
    {
        var installed = new[] { new FoundProgram("OBS Studio", @"c:\x\obs64.exe"), new FoundProgram("Chrome", @"c:\x\chrome.exe") };
        Assert.Equal(new[] { "OBS Studio" }, ProgramListing.Choices(Array.Empty<AppAudioEntry>(), installed, "studio").Select(c => c.Name));
        Assert.Equal(new[] { "OBS Studio" }, ProgramListing.Choices(Array.Empty<AppAudioEntry>(), installed, "OBS64").Select(c => c.Name));
        Assert.Empty(ProgramListing.Choices(Array.Empty<AppAudioEntry>(), installed, "nothing"));
    }

    [Fact]
    public void Clean_DedupesByPathKeepingTheFirstName()
    {
        var found = new[]
        {
            new FoundProgram("Spotify", @"C:\Apps\Spotify.exe"),
            new FoundProgram("spotify", @"c:\apps\SPOTIFY.EXE"),
            new FoundProgram("Other", @"C:\Apps\Other.exe"),
        };
        var clean = InstalledPrograms.Clean(found, Windows);
        Assert.Equal(new[] { "Spotify", "Other" }, clean.Select(p => p.Name));
    }

    [Fact]
    public void Clean_DropsSetupToolsWindowsProgramsAndNonExe()
    {
        var found = new[]
        {
            new FoundProgram("Uninstall Foo", @"C:\Apps\Foo\anything.exe"),
            new FoundProgram("Foo uninstaller", @"C:\Apps\Foo\unins000.exe"),
            new FoundProgram("Foo setup", @"C:\Apps\Foo\Setup.exe"),
            new FoundProgram("Notepad", @"C:\Windows\System32\notepad.exe"),
            new FoundProgram("A document", @"C:\Docs\readme.txt"),
            new FoundProgram("Foo", @"C:\Apps\Foo\foo.exe"),
            new FoundProgram("Windows tool in other folder", @"C:\WindowsApps2\tool.exe"), // starts with "Windows" but is not the folder
        };
        Assert.Equal(new[] { "Foo", "Windows tool in other folder" }, InstalledPrograms.Clean(found, Windows).Select(p => p.Name));
    }
}

using Avalonia.Input;
using OmniControl.App.Shared.Services;

namespace OmniControl.Tests;

/// The shared undo history: named steps, undo and redo, merging a continuous change, the length cap, what is released, the keys.
public class UndoHistoryTests
{
    private long _clock;

    private UndoHistory Make(int limit = 100) => new(limit, () => _clock);

    private static void Set(UndoHistory history, List<int> values, int value, string name, object? key = null)
    {
        var was = values.ToList();
        values.Add(value);
        var now = values.ToList();
        history.Record(name, () => Replace(values, was), () => Replace(values, now), key);
    }

    private static void Replace(List<int> values, List<int> with)
    {
        values.Clear();
        values.AddRange(with);
    }

    [Fact]
    public void UndoRedo_WalkTheStepsAndNameThem()
    {
        var history = Make();
        var values = new List<int>();
        Assert.Equal("Undo", history.UndoText);
        Assert.False(history.UndoCommand.CanExecute(null));
        Set(history, values, 1, "Add one");
        Set(history, values, 2, "Add two");
        Assert.Equal("Undo Add two", history.UndoText);
        history.Undo();
        Assert.Equal(new[] { 1 }, values);
        Assert.Equal("Redo Add two", history.RedoText);
        Assert.True(history.RedoCommand.CanExecute(null));
        history.Redo();
        Assert.Equal(new[] { 1, 2 }, values);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void NewStep_DropsTheUndoneAndReleasesThem()
    {
        var history = Make();
        var released = new List<string>();
        history.Record("a", () => { }, () => { }, forget: () => released.Add("a"));
        history.Record("b", () => { }, () => { }, forget: () => released.Add("b"));
        history.Undo();
        history.Record("c", () => { }, () => { });
        Assert.Equal(new[] { "b" }, released);
        Assert.False(history.CanRedo);
        history.Clear();
        Assert.False(history.CanUndo);
        Assert.Equal(new[] { "b" }, released); // shown steps are their owner's to release
    }

    [Fact]
    public void SameKey_WithinTheWindow_IsOneStep()
    {
        var history = Make();
        var values = new List<int>();
        Set(history, values, 1, "Change Gain", "gain");
        _clock += 200;
        Set(history, values, 2, "Change Gain", "gain");
        _clock += (long)Timing.UndoMergeWindow.TotalMilliseconds + 1;
        Set(history, values, 3, "Change Gain", "gain");
        history.Undo();
        Assert.Equal(new[] { 1, 2 }, values);
        history.Undo();
        Assert.Empty(values);
        Assert.False(history.CanUndo);
        history.Redo();
        Assert.Equal(new[] { 1, 2 }, values);
    }

    [Fact]
    public void AfterAnUndo_TheNextChangeIsItsOwnStep()
    {
        var history = Make();
        var values = new List<int>();
        Set(history, values, 1, "Change", "k");
        Set(history, values, 2, "Change", "k");
        history.Undo();
        Set(history, values, 5, "Change", "k");
        history.Undo();
        Assert.Empty(values);
    }

    [Fact]
    public void Cap_DropsTheOldest()
    {
        var history = Make(limit: 2);
        var values = new List<int>();
        for (var i = 1; i <= 3; i++) Set(history, values, i, $"Add {i}");
        history.Undo();
        history.Undo();
        Assert.False(history.CanUndo);
        Assert.Equal(new[] { 1 }, values);
    }

    [Fact]
    public void RecordWhileApplying_IsIgnored()
    {
        var history = Make();
        history.Record("a", () => history.Record("echo", () => { }, () => { }), () => { });
        history.Undo();
        Assert.False(history.CanUndo);
        Assert.Equal("Redo a", history.RedoText);
    }

    [Fact]
    public void Keys_ZUndoes_YAndShiftZRedo()
    {
        var history = Make();
        var values = new List<int>();
        Set(history, values, 1, "Add");
        Assert.True(history.HandleKey(Key.Z, KeyModifiers.Control));
        Assert.Empty(values);
        Assert.True(history.HandleKey(Key.Y, KeyModifiers.Control));
        Assert.Equal(new[] { 1 }, values);
        history.Undo();
        Assert.True(history.HandleKey(Key.Z, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.Equal(new[] { 1 }, values);
        Assert.False(history.HandleKey(Key.Z, KeyModifiers.None));
    }
}

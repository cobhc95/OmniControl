using Avalonia.Controls;
using OmniControl.App.Pages;

namespace OmniControl.Tests;

/// <summary>The wheel changes a ‹ value › selector only while the pointer is over the selector, never over the label or empty space.</summary>
public class ValueWheelSelectorTests
{
    private static (Panel row, TextBlock label, Border selector, TextBlock value, Panel empty) Row()
    {
        var label = new TextBlock();
        var value = new TextBlock();
        var selector = new Border { Child = value };
        ValueWheel.SetSelector(selector, true);
        var empty = new Panel();
        var row = new Panel();
        row.Children.Add(label);
        row.Children.Add(selector);
        row.Children.Add(empty);
        return (row, label, selector, value, empty);
    }

    [Fact]
    public void Over_the_selector_or_its_value_changes_the_value()
    {
        var (_, _, selector, value, _) = Row();
        Assert.True(ValueWheel.IsOverSelector(selector));
        Assert.True(ValueWheel.IsOverSelector(value));
    }

    [Fact]
    public void Over_the_label_or_empty_space_is_left_to_the_page()
    {
        var (row, label, _, _, empty) = Row();
        Assert.False(ValueWheel.IsOverSelector(label));
        Assert.False(ValueWheel.IsOverSelector(empty));
        Assert.False(ValueWheel.IsOverSelector(row));
        Assert.False(ValueWheel.IsOverSelector(null));
    }
}

using System.Globalization;
using System.Text;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace OmniControl.Screenshots;

/// Writes what the app's styles give a control of each class combination in each state (hover, press, keyboard focus, disabled, checked):
/// the looks and sizes of the control and every part of its template, as text, so two runs (before and after a change to the master styles)
/// can be compared line by line. Each line of the combinations file is "Type class class…", optionally followed by " @ " and the
/// ancestors it sits in, outermost first ("Button selector @ Border.toolrow"); '#' starts a comment.
internal static class StyleProbe
{
    private static readonly string[] Properties =
    {
        "Background", "BorderBrush", "BorderThickness", "Foreground", "CornerRadius", "Padding", "Margin", "Width", "Height", "MinWidth",
        "MinHeight", "MaxWidth", "MaxHeight", "FontSize", "FontWeight", "FontStyle", "Opacity", "IsVisible", "HorizontalAlignment",
        "VerticalAlignment", "HorizontalContentAlignment", "VerticalContentAlignment", "TextDecorations", "TextWrapping", "TextTrimming",
        "MaxLines", "LineHeight", "TextAlignment", "RenderTransform", "Cursor", "Stroke", "StrokeThickness", "Fill", "ClipToBounds",
        "BoxShadow", "FocusAdorner", "ZIndex", "Stretch", "IsHitTestVisible", "Focusable",
    };

    private static readonly Dictionary<string, Func<Control>> Types = new()
    {
        ["Button"] = () => new Button { Content = new TextBlock { Text = "Ab" } },
        ["ToggleButton"] = () => new ToggleButton { Content = new TextBlock { Text = "Ab" } },
        ["TextBlock"] = () => new TextBlock { Text = "Ab" },
        ["Border"] = () => new Border { Child = new TextBlock { Text = "Ab" } },
        ["WrapPanel"] = () => new WrapPanel(),
        ["StackPanel"] = () => new StackPanel(),
        ["Grid"] = () => new Grid(),
        ["Panel"] = () => new Panel(),
        ["ListBoxItem"] = () => new ListBoxItem(),
        ["FlyoutPresenter"] = () => new FlyoutPresenter(),
    };

    public static void Run(string combosFile, string outFile)
    {
        var host = new StackPanel();
        var window = new Window { Width = 480, Height = 320, Content = host };
        window.Show();
        var text = new StringBuilder();
        foreach (var raw in File.ReadAllLines(combosFile))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var halves = line.Split(" @ ");
            var words = halves[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var state in States(words[0]))
            {
                var control = Make(words[0], words.Skip(1));
                host.Children.Add(Wrap(control, halves.Length > 1 ? halves[1] : ""));
                Camera.Settle(1);
                // A transition would leave a state's values half way, different from run to run
                foreach (var part in new[] { control }.Concat(control.GetVisualDescendants().OfType<Control>())) part.Transitions = null;
                Apply(control, state);
                Camera.Settle(2);
                text.Append("== ").Append(line).Append(" | ").AppendLine(state);
                Dump(text, control);
                host.Children.Clear();
            }
        }
        window.Close();
        File.WriteAllText(outFile, text.ToString());
        Console.WriteLine($"Probe written: {outFile}");
    }

    private static IEnumerable<string> States(string type) => type switch
    {
        "Button" => new[] { "", "pointerover", "pointerover pressed", "focus focus-visible", "focus focus-visible pointerover", "disabled" },
        "ToggleButton" => new[] { "", "pointerover", "pointerover pressed", "focus focus-visible", "disabled", "checked", "checked pointerover" },
        _ => new[] { "", "pointerover" },
    };

    private static Control Make(string type, IEnumerable<string> classes)
    {
        var control = Types[type]();
        control.Classes.AddRange(classes);
        return control;
    }

    /// Puts the control inside its ancestors ("Border.toolrow WrapPanel.sentence"), outermost first.
    private static Control Wrap(Control control, string ancestors)
    {
        var inner = control;
        foreach (var part in ancestors.Split(' ', StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            var names = part.Split('.');
            var outer = Make(names[0], names.Skip(1));
            switch (outer)
            {
                case ContentControl content: content.Content = inner; break;
                case Border border: border.Child = inner; break;
                case Panel panel: panel.Children.Add(inner); break;
                case Decorator decorator: decorator.Child = inner; break;
            }
            inner = outer;
        }
        return inner;
    }

    private static void Apply(Control control, string state)
    {
        foreach (var name in state.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (name == "disabled") control.IsEnabled = false;
            else if (name == "checked" && control is ToggleButton toggle) toggle.IsChecked = true;
            else ((IPseudoClasses)control.Classes).Set(":" + name, true);
        }
    }

    private static void Dump(StringBuilder text, Control control)
    {
        foreach (var visual in new[] { (Visual)control }.Concat(control.GetVisualDescendants()))
        {
            var depth = visual.GetVisualAncestors().TakeWhile(a => a != control).Count() + (visual == control ? 0 : 1);
            text.Append(' ', 2 + depth * 2).Append(visual.GetType().Name);
            if (visual is StyledElement { Name: { Length: > 0 } partName }) text.Append('#').Append(partName);
            if (visual is StyledElement { Classes.Count: > 0 } styled) text.Append('.').Append(string.Join('.', styled.Classes));
            text.Append(" size=").Append(Show(visual.Bounds.Size));
            foreach (var name in Properties)
            {
                if (AvaloniaPropertyRegistry.Instance.FindRegistered(visual, name) is not { } property) continue;
                var value = visual.GetValue(property);
                var fallback = property.GetMetadata(visual.GetType()) is IStyledPropertyMetadata meta ? meta.DefaultValue : null;
                if (Equals(value, fallback)) continue;
                text.Append(' ').Append(name).Append('=').Append(Show(value));
            }
            text.AppendLine();
        }
    }

    private static string Show(object? value) => value switch
    {
        null => "null",
        ISolidColorBrush brush => $"{brush.Color}@{brush.Opacity.ToString("0.###", CultureInfo.InvariantCulture)}",
        IBrush brush => brush.GetType().Name,
        ITransform transform => transform.Value.ToString(),
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        Size size => $"{size.Width.ToString("0.#", CultureInfo.InvariantCulture)}x{size.Height.ToString("0.#", CultureInfo.InvariantCulture)}",
        ControlTheme or ITemplate => value.GetType().Name,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };
}

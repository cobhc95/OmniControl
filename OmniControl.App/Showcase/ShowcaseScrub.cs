using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace OmniControl.App.Showcase;

/// <summary>
/// The placeholder layer: while a picture is taken, every text that shows the user and machine names, an email, an IP or a MAC address
/// (and, unless the export shows real names, this PC's device and monitor names) shows a stand-in instead. A text the stand-in cannot
/// reach (words built from inline runs) is blurred instead, and a text box holding any of it is hidden behind password dots. Every change
/// is a temporary value over the real one (a binding is never written to), <see cref="Dispose"/> puts everything back, and
/// <see cref="Report"/> says what was done, by kind and never by content, for <c>privacy-report.txt</c>.
/// </summary>
internal sealed partial class ShowcaseScrub : IDisposable
{
    private const string Names = "device or monitor name", Personal = "user, machine or profile name", Mail = "email", Address = "IP address", Hardware = "MAC address";
    private static readonly string[] Stand = { "Desk speakers", "Headphones", "Desk lamp", "Living room TV", "Microphone", "Webcam", "Studio monitor", "Soundbar" };
    private const double BlurRadius = 8;

    private readonly List<(string Real, string Fake, string Kind)> _names;
    private readonly List<IDisposable> _undo = new();
    private readonly SortedDictionary<string, int> _done = new(StringComparer.Ordinal);

    /// <param name="privateNames">Device and monitor names to replace; empty when the export keeps real names.</param>
    public ShowcaseScrub(IEnumerable<string> privateNames)
    {
        var personal = new[] { Environment.UserName, Environment.MachineName, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        _names = privateNames.Where(n => n.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((n, i) => (n, i < Stand.Length ? Stand[i] : $"Device {i + 1}", Names))
            .Concat(personal.Where(n => n.Length >= 3).Select(n => (n, "user", Personal)))
            .OrderByDescending(p => p.Item1.Length).ToList();
    }

    /// <summary>What was replaced, blurred or hidden in this picture, one line per kind with its count (empty: nothing needed it).</summary>
    public IReadOnlyList<string> Report => _done.Select(p => $"{p.Key} x{p.Value}").ToList();

    /// <summary>Covers every text under <paramref name="root"/>.</summary>
    public void Apply(Visual root)
    {
        foreach (var visual in root.GetVisualDescendants().Prepend(root))
        {
            switch (visual)
            {
                case TextBlock { Inlines.Count: > 0 } runs:
                    if (Clean(string.Concat(runs.Inlines.OfType<Run>().Select(r => r.Text)), out var inlineKinds) is not null)
                    {
                        Cover(runs.SetValue(Visual.EffectProperty, new BlurEffect { Radius = BlurRadius }, BindingPriority.Animation));
                        Note("blurred", inlineKinds);
                    }
                    break;
                case TextBlock { Text: { Length: > 0 } text } block when Clean(text, out var kinds) is { } cleaned:
                    Cover(block.SetValue(TextBlock.TextProperty, cleaned, BindingPriority.Animation));
                    Note("replaced", kinds);
                    break;
                case TextBox { Text: { Length: > 0 } typed } box when Clean(typed, out var seen) is not null:
                    Cover(box.SetValue(TextBox.PasswordCharProperty, '*', BindingPriority.Animation)); // hides it; the text and its binding stay untouched
                    Note("hidden behind dots", seen);
                    break;
            }
        }
    }

    /// <summary>The text with its personal parts replaced, or null when it holds none; <paramref name="kinds"/> says which parts.</summary>
    private string? Clean(string text, out List<string> kinds)
    {
        kinds = new List<string>();
        var result = text;
        foreach (var (real, fake, kind) in _names)
        {
            var next = result.Replace(real, fake, StringComparison.OrdinalIgnoreCase);
            if (next != result) kinds.Add(kind);
            result = next;
        }
        result = Replace(UserPath(), result, @"C:\Users\user", Personal, kinds); // another account's profile folder
        result = Replace(Email(), result, "you@example.com", Mail, kinds);
        result = Replace(Ip(), result, "192.168.0.10", Address, kinds);
        result = Replace(Mac(), result, "00:11:22:33:44:55", Hardware, kinds);
        return result == text ? null : result;
    }

    /// <summary>A stand-in that already stands in (the text is the stand-in itself) counts as no change.</summary>
    private static string Replace(Regex pattern, string text, string stand, string kind, List<string> kinds)
    {
        var next = pattern.Replace(text, stand);
        if (next != text) kinds.Add(kind);
        return next;
    }

    private void Note(string what, IEnumerable<string> kinds)
    {
        foreach (var kind in kinds.Distinct())
        {
            var key = $"{what}: {kind}";
            _done[key] = _done.GetValueOrDefault(key) + 1;
        }
    }

    private void Cover(IDisposable? change)
    {
        if (change is not null) _undo.Add(change);
    }

    public void Dispose()
    {
        foreach (var change in _undo) change.Dispose();
        _undo.Clear();
    }

    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\\s""]+", RegexOptions.IgnoreCase)] private static partial Regex UserPath();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+")] private static partial Regex Email();

    [GeneratedRegex(@"\b\d{1,3}(\.\d{1,3}){3}\b")] private static partial Regex Ip();

    [GeneratedRegex(@"\b([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b")] private static partial Regex Mac();
}

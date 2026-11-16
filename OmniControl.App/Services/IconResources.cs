using Avalonia.Controls;
using Avalonia;
using Avalonia.Media;

namespace OmniControl.App.Services;

/// <summary>
/// Turns an icon into a drawable shape: an entry of a master list (an action, a state) names its own glyph by the resource key of a
/// <c>StreamGeometry</c> in <c>Styles/Brushes.axaml</c>; when it names none, the glyph of the page it belongs to (path data) stands in.
/// </summary>
public static class IconResources
{
    /// <summary>The glyph resource with this key; null when there is none (no key, or no running application).</summary>
    public static Geometry? Find(string? key) =>
        key is not null && Application.Current?.TryFindResource(key, out var found) == true ? found as Geometry : null;

    /// <summary>The entry's own glyph, or else the page's one (<paramref name="pagePath"/>, path data); null when neither exists.</summary>
    public static Geometry? Of(string? ownKey, string? pagePath) =>
        Find(ownKey) ?? (string.IsNullOrEmpty(pagePath) ? null : Geometry.Parse(pagePath));
}

using System.Text.Json.Serialization;

namespace OmniControl.App.AppLists;

/// <summary>Where something works: in every app, only in the apps of a list, or in every app except those of a list.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScopeMode { All, Only, Except }

/// <summary>
/// A stored apps scope: the mode and the list it names (empty for <see cref="ScopeMode.All"/>). Settings classes keep one wherever
/// something can be limited to apps; null there means "not limited" (or, on a card, "the same as its group").
/// </summary>
public sealed record AppScopeSetting(ScopeMode Mode = ScopeMode.All, string ListId = "")
{
    public static readonly AppScopeSetting All = new();

    /// <summary>True when this scope limits where something works (a hand-edited file with a mode but no list limits nothing).</summary>
    public bool IsLimited => Mode != ScopeMode.All && ListId.Length > 0;

    /// <summary>The one rule: whether something with this scope works in a program that is (or is not) in the scope's list.</summary>
    public bool Allows(bool programIsListed) => Mode switch
    {
        ScopeMode.Only => programIsListed,
        ScopeMode.Except => !programIsListed,
        _ => true,
    };
}

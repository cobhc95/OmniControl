using System.Text.Json.Serialization;

namespace OmniControl.App.Features.AppLists;

/// Where something works: in every app, only in the apps of a list, or in every app except those of a list.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScopeMode { All, Only, Except }

/// A stored apps scope: the mode and the list it names (empty for <see cref="ScopeMode.All"/>). Settings classes keep one wherever
/// something can be limited to apps; null there means "not limited" (or, on a card, "the same as its group").
public sealed record AppScopeSetting(ScopeMode Mode = ScopeMode.All, string ListId = "")
{
    public static readonly AppScopeSetting All = new();

    /// True when this scope limits where something works (a hand-edited file with a mode but no list limits nothing).
    public bool IsLimited => Mode != ScopeMode.All && ListId.Length > 0;

    /// The one rule: whether something with this scope works in a program that is (or is not) in the scope's list.
    public bool Allows(bool programIsListed) => Mode switch
    {
        ScopeMode.Only => programIsListed,
        ScopeMode.Except => !programIsListed,
        _ => true,
    };
}

using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Audio;

namespace OmniControl.App.Fx.Presets;

/// <summary>One preset row of the manager: a built-in one (greyed, tagged, never changed) or one of the user's own.</summary>
public sealed partial class PresetRowItem : ObservableObject
{
    public PresetRowItem(FxKind kind, string name, PresetContent content, bool builtIn, DateTime? saved)
    {
        Kind = kind;
        Name = name;
        Content = content;
        BuiltIn = builtIn;
        DateText = saved is { } when ? when.ToString("d MMM yyyy") : string.Empty;
    }

    public FxKind Kind { get; }
    public string Name { get; }
    public PresetContent Content { get; }
    public bool BuiltIn { get; }

    /// <summary>The effect it belongs to, shown in search results where kinds are mixed.</summary>
    public string Where => Kind.Name;

    public string DateText { get; }

    /// <summary>Search results mix kinds, so each row says which effect it belongs to.</summary>
    public bool ShowWhere { get; init; }

    public string NameTip => BuiltIn ? "Comes with the app: it cannot be renamed, moved or deleted" : "Double-click to rename";

    /// <summary>The name is being typed in place (the user's own presets only).</summary>
    [ObservableProperty] private bool _isEditing;

    [ObservableProperty] private string _editText = string.Empty;

    public bool Is(FxKind kind, string name) => Kind.Kind == kind.Kind && PresetRules.Same(Name, name);
}

/// <summary>One effect kind in the manager's list, with how many presets it has.</summary>
public sealed partial class PresetKindItem : ObservableObject
{
    public PresetKindItem(FxKind kind) => Kind = kind;

    public FxKind Kind { get; }
    public string Title => Kind.Name;

    [ObservableProperty] private int _count;
}

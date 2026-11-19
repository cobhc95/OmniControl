namespace OmniControl.App.Shared.Services;

/// Which other PC a master-list entry runs on, as the lists name it: the section words with the PC's name ("Other PCs · Laptop")
/// and, when the PC has a nickname, its real name. Both are read live from the owner, so a rename shows at once. Drawn by <c>Controls/RemoteSectionTag</c>.
public sealed class RemotePcLabel
{
    private readonly string _section;
    private readonly Func<string> _name;
    private readonly Func<string?> _realName;

    /// <param name="section">The section's name ("Other PCs").</param>
    /// <param name="name">The PC's name as the user sees it (its nickname, else its real name).</param>
    /// <param name="realName">The PC's real name when it has a nickname; null when the name above already is the real one.</param>
    public RemotePcLabel(string section, Func<string> name, Func<string?> realName) => (_section, _name, _realName) = (section, name, realName);

    /// The bold accent words: "Other PCs · Laptop".
    public string Section => $"{_section} · {_name()}";

    /// The muted real name, or null.
    public string? RealName => _realName() is { Length: > 0 } real ? real : null;

    public bool HasRealName => RealName is not null;
}

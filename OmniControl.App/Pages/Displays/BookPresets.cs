using OmniControl.App.Audio;
using OmniControl.App.Presets;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// A Displays preset book (<see cref="IPresetBook"/>: the Software group's picture presets, the Hardware group's monitor presets) as a
/// preset group of the preset manager: the saved presets in their order, the one the live settings show marked "in use", a monitor
/// preset's monitors as tags (connected ones green). Adding saves the live settings under a first free name; the rest is the book's.
/// </summary>
internal sealed class BookPresets : IPresetProvider
{
    private readonly IPresetBook _book;

    public BookPresets(IPresetBook book)
    {
        _book = book;
        Words = new PresetWords("Apply", "Apply preset", book.Text.AddText, book.Text.AddTip);
    }

    public PresetWords Words { get; }

    public PresetSwitch? Switch => null;

    public bool CanDuplicate => false;

    public bool CanExchangeFiles => false;

    public string? AddUnavailableReason => _book.UnavailableReason;

    public IReadOnlyList<PresetEntry> Entries
    {
        get
        {
            var shown = _book.Shown;
            return _book.Presets.Select(p => new PresetEntry(p.Id, p.Name)
            {
                InUse = shown is not null && PresetRules.Same(shown, p.Name),
                Tags = _book.PartsOf(p.Name).Select(part => new TagItem(part.Connected ? part.Name : $"{part.Name} (not connected)", part.Connected ? TagTone.Good : TagTone.Neutral)).ToList(),
            }).ToList();
        }
    }

    public PresetEntry? Find(string id) => Entries.FirstOrDefault(e => e.Id == id);

    private string? NameOf(string id) => _book.Presets.FirstOrDefault(p => p.Id == id)?.Name;

    public string? Apply(string id)
    {
        if (NameOf(id) is { } name) _book.Apply(name);
        return null;
    }

    public PresetChange Add()
    {
        if (_book.UnavailableReason is { } reason) return PresetChange.Refused(reason);
        var name = PresetRules.FirstFree(_book.Text.NewName, _book.Names);
        _book.SaveCurrent(name);
        return _book.Presets.FirstOrDefault(p => PresetRules.Same(p.Name, name)) is { } made ? PresetChange.Made(made.Id) : PresetChange.Done;
    }

    public PresetChange Rename(string id, string name) =>
        NameOf(id) is { } from && _book.Rename(from, name) is { } problem ? PresetChange.Refused(problem) : PresetChange.Done;

    public PresetChange Move(string id, int index)
    {
        if (NameOf(id) is { } name) _book.Move(name, index);
        return PresetChange.Done;
    }

    public Action? Delete(string id) => NameOf(id) is { } name ? _book.Delete(name) : null;

    public PresetChange Duplicate(string id) => PresetChange.Done;

    public byte[]? Export(string id) => null;

    public PresetChange Import(string fileName, byte[] bytes) => PresetChange.Done;

    public event Action? Changed
    {
        add => _book.Changed += value;
        remove => _book.Changed -= value;
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Devices;
using OmniControl.App.Services;
using OmniControl.App.Dialogs;

namespace OmniControl.App.Pages.Devices;

/// <summary>
/// The Smart Devices page (docs/mockups/smart-devices-setup-vs-ready.html, device-multiple-methods.html): the devices on the left with a switch
/// each (or "set-up"), the chosen one on the right, ready or in a method's set-up. The list takes several rows at once (Ctrl, Shift, Ctrl+A)
/// and its right-click menu copies, cuts, pastes, duplicates, merges and deletes them. The devices, their actions and their commands are
/// <see cref="DeviceService"/>'s.
/// </summary>
public sealed partial class DevicesPageViewModel : PageViewModel, IDisposable
{
    private readonly DeviceService _service;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Action<string> _report;
    private readonly Func<Avalonia.Visual, Icons.IconRequest, Task<Icons.IconPick?>> _chooseIcon;

    /// <summary>The devices copied (or cut) with the list's menu or Ctrl+C / Ctrl+X; a cut is only moved by Paste.</summary>
    private readonly List<DeviceRecord> _clipboard = new();
    private bool _clipboardIsCut;

    public override string Title => DeviceService.Category;

    public override string? NavGroup => NavGroups.Devices;

    public override string Icon => "M8,2 H10 V7 H8 Z M14,2 H16 V7 H14 Z M6,7 H18 V13 A6,6 0 0 1 13,18.9 V22 H11 V18.9 A6,6 0 0 1 6,13 Z M8,9 V13 A4,4 0 0 0 16,13 V9 Z";

    public override string Keywords => "devices smart plug bulb socket outlet tapo tp-link p100 p110 l530 kasa python-kasa profile template switch power on off lamp router ip address simulation emulator android adb bluestacks ldplayer mumu memu app steps record teach icon method methods merge";

    public ObservableCollection<DeviceItem> Items { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelected))] private DeviceItem? _selected;

    public bool NoDevices => Items.Count == 0;
    public bool HasSelected => Selected is not null;

    /// <param name="chooseIcon">The shared icon picker (a device's "Change icon…").</param>
    public DevicesPageViewModel(DeviceService service, Func<DialogMessage, Task<bool>> ask, Action<string> report,
        Func<Avalonia.Visual, Icons.IconRequest, Task<Icons.IconPick?>> chooseIcon)
    {
        _service = service;
        _ask = ask;
        _report = report;
        _chooseIcon = chooseIcon;
        foreach (var record in service.Records) Items.Add(Make(record));
        Selected = Items.FirstOrDefault();
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoDevices));
    }

    // The checks of Python, python-kasa, adb and the emulators run when the page is shown, not at start-up.
    public override void OnShown()
    {
        if (!StartupMode.Inert) _ = ProbeThenCheckAsync(); // the probes start programs (Python, adb, the emulators): never in a screenshot export
    }

    private async Task ProbeThenCheckAsync()
    {
        var probes = Task.WhenAll(_service.Setup.RefreshAsync(), _service.Simulation.RefreshAsync());
        await probes;
        await _service.CheckWhenShownAsync();
    }

    /// <summary>The device whose power state this is becomes the selected one, so its detail shows.</summary>
    public override void Reveal(string stateId)
    {
        if (Items.FirstOrDefault(i => DeviceService.StateId(i.Record) == stateId) is { } item) Selected = item;
    }

    private DeviceItem Make(DeviceRecord record) => new(record, _service, _report, _ask, DeleteAsync, _chooseIcon);

    [RelayCommand]
    private void AddDevice()
    {
        var item = Make(_service.Add());
        Items.Add(item);
        Selected = item;
    }

    /// <summary>The list follows the service's order (after a paste or a move), keeping the rows that stay.</summary>
    private void SyncItems()
    {
        var kept = Items.ToDictionary(i => i.Record);
        foreach (var gone in Items.Where(i => !_service.Has(i.Record)).ToList()) gone.Dispose();
        var selected = Selected;
        Items.Clear();
        foreach (var record in _service.Records) Items.Add(kept.TryGetValue(record, out var item) ? item : Make(record));
        Selected = selected is not null && Items.Contains(selected) ? selected : Items.FirstOrDefault();
    }

    private IReadOnlyList<DeviceItem> InOrder(IEnumerable<DeviceItem> rows) => rows.Where(Items.Contains).Distinct().OrderBy(Items.IndexOf).ToList();

    // ---- The list's right-click menu and keys ----

    /// <summary>The right-click menu for <paramref name="rows"/>; <paramref name="openMerge"/> shows the list of devices to merge them into.</summary>
    public IReadOnlyList<MenuRowItem> RowMenu(IReadOnlyList<DeviceItem> rows, Action openMerge) => new[]
    {
        MenuRow("Copy", () => Copy(rows)),
        MenuRow("Cut", () => Cut(rows)),
        new MenuRowItem("Paste", string.Empty, false, "Pastes below. A cut device moves; a copied one is copied, without its password.",
            new RelayCommand(() => PasteBelow(rows), () => _clipboard.Count > 0)),
        MenuRow("Duplicate", () => Duplicate(rows)),
        new MenuRowItem("Merge into…", string.Empty, false, "Joins these devices' methods to another device (set-ups kept) and removes them.",
            new RelayCommand(openMerge, () => Items.Except(rows).Any()), RuleAbove: true),
        new MenuRowItem("Delete", string.Empty, false, string.Empty, new RelayCommand(() => _ = DeleteRowsAsync(rows)), RuleAbove: true),
    };

    /// <summary>The "Merge into…" list: every device not among <paramref name="rows"/>.</summary>
    public IReadOnlyList<MenuRowItem> MergeTargets(IReadOnlyList<DeviceItem> rows) =>
        Items.Except(rows).Select(target => new MenuRowItem(target.Name, string.Empty, false, $"Their methods go after {target.Name}'s.",
            new RelayCommand(() => _ = MergeAsync(rows, target)))).ToList();

    private static MenuRowItem MenuRow(string text, Action run) => new(text, string.Empty, false, string.Empty, new RelayCommand(run));

    public void Copy(IReadOnlyList<DeviceItem> rows)
    {
        _clipboard.Clear();
        _clipboard.AddRange(InOrder(rows).Select(r => r.Record));
        _clipboardIsCut = false;
    }

    /// <summary>A cut only marks the devices; Paste moves them (nothing is removed until then).</summary>
    public void Cut(IReadOnlyList<DeviceItem> rows)
    {
        Copy(rows);
        _clipboardIsCut = true;
    }

    /// <summary>Puts the clipboard's devices below the last of <paramref name="rows"/>: a cut moves them, a copy adds copies without passwords.</summary>
    public void PasteBelow(IReadOnlyList<DeviceItem> rows)
    {
        if (_clipboard.Count == 0) return;
        var below = InOrder(rows).LastOrDefault();
        if (_clipboardIsCut)
        {
            var devices = _service.Records;
            var moving = _clipboard.Where(_service.Has).Select(_service.IndexOf).ToList();
            var stay = devices.Where(d => !_clipboard.Contains(d)).ToList();
            var insertAt = below is null ? stay.Count : stay.IndexOf(below.Record) + 1;
            if (below is not null && _clipboard.Contains(below.Record)) insertAt = stay.Count(d => _service.IndexOf(d) < _service.IndexOf(below.Record));
            _service.MoveDevices(moving, insertAt);
            _clipboard.Clear();
            _clipboardIsCut = false;
            SyncItems();
            return;
        }
        var at = below is null ? _service.Records.Count : _service.IndexOf(below.Record) + 1;
        AddCopies(_clipboard.ToList(), at);
    }

    /// <summary>Each device gets a copy right after it.</summary>
    public void Duplicate(IReadOnlyList<DeviceItem> rows)
    {
        var copies = InOrder(rows).Select(row => _service.Duplicate(row.Record, _service.IndexOf(row.Record) + 1)).ToList();
        Copied(copies);
    }

    private void AddCopies(IReadOnlyList<DeviceRecord> sources, int at)
    {
        var copies = new List<DeviceRecord>();
        foreach (var source in sources) copies.Add(_service.Duplicate(source, at + copies.Count));
        Copied(copies);
    }

    /// <summary>The copies are shown; when one needs its sign-in typed again, the status line says so once.</summary>
    private void Copied(IReadOnlyList<DeviceRecord> copies)
    {
        SyncItems();
        if (copies.Count > 0) Selected = Items.FirstOrDefault(i => i.Record == copies[0]);
        if (copies.Any(_service.NeedsSignIn)) _report("Passwords are never copied: type the sign-in again in the copy's setup.");
    }

    /// <summary>Deletes the rows (one question for all of them), with their actions and shortcuts.</summary>
    public async Task DeleteRowsAsync(IReadOnlyList<DeviceItem> rows)
    {
        var doomed = InOrder(rows);
        if (doomed.Count == 0) return;
        var question = doomed.Count == 1 ? Confirm.Delete("device", doomed[0].Record.Name) : Confirm.Delete($"{doomed.Count} devices", null, "device");
        if (!await _ask(question)) return;
        foreach (var item in doomed) RemoveItem(item);
    }

    private async Task DeleteAsync(DeviceItem item)
    {
        if (!await _ask(Confirm.Delete("device", item.Record.Name))) return;
        RemoveItem(item);
    }

    private void RemoveItem(DeviceItem item)
    {
        var at = Items.IndexOf(item);
        _clipboard.Remove(item.Record);
        _service.Remove(item.Record);
        Items.Remove(item);
        item.Dispose();
        if (Selected is null || !Items.Contains(Selected)) Selected = Items.Count == 0 ? null : Items[Math.Clamp(at, 0, Items.Count - 1)];
    }

    /// <summary>Joins the rows' methods to <paramref name="target"/> (asks first) and removes the rows; their shortcuts move to the "via" actions.</summary>
    private async Task MergeAsync(IReadOnlyList<DeviceItem> rows, DeviceItem target)
    {
        var from = InOrder(rows).Where(r => r != target).ToList();
        if (from.Count == 0) return;
        if (from.Append(target).Any(i => _service.RuntimeOf(i.Record).Busy)) { _report("A device is still busy with a command. Merge it when it is done."); return; }
        var names = string.Join(", ", from.Select(f => $"\"{f.Name}\""));
        if (!await _ask(new DialogMessage("Merge devices?", $"{names} will become methods of \"{target.Name}\" (set-ups and passwords kept) and be removed from the list.", "Merge")))
            return;
        _service.Merge(from.Select(f => f.Record).ToList(), target.Record);
        foreach (var item in from) _clipboard.Remove(item.Record);
        SyncItems();
        target.Reload();
        Selected = target;
    }

    public void Dispose()
    {
        foreach (var item in Items) item.Dispose();
    }
}

using OmniControl.App.Audio;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The Wiring tab's Virtual cables group: every virtual device the PC has (<see cref="AudioEffectsSupport.Found"/>), each cable pair's
/// switch (stored by the mixer; <see cref="SoundModeService"/> then offers only the allowed ones to everything else), what the engine's
/// plan uses it for or why it is not used, the user's marked pairs, and the steps that get more cables.
/// </summary>
public sealed partial class VolumePageViewModel
{
    private const string CablesKeywords = "virtual cable cables vb vb-cable hi-fi voicemeeter voicemod line manage allow switch mark";
    private const string UsePrefix = "Use for: ", NotUsedPrefix = "Not used: ";
    private const string MasterBusUse = "master bus", AutomaticUse = "Automatic", HeldUse = "kept for its device";
    private const string FreeState = "Free", InUseState = "In use";

    /// <summary>The Virtual cables group under Microphones (set in the constructor).</summary>
    public CableGroup WiringCables { get; }

    private readonly Dictionary<string, CableRow> _cableRows = new();

    private CableGroup NewWiringCables() => new(new CableMarkMenu(MarkCable, OpenCableMark), MoreCablesAction() with { Label = "Get more cables" })
    {
        ExpandedByDefault = true,
        SummaryBesideName = true,
        Tip = "Virtual cables found on this PC. Switch one off to keep OmniControl from using it.",
    };

    /// <summary>Brings the list in line with what the PC has, the user's switches and the engine's plan.</summary>
    private void SyncCables()
    {
        var found = _modes.Current?.Found ?? Array.Empty<FoundCable>();
        foreach (var gone in _cableRows.Keys.Where(id => found.All(f => f.Id != id)).ToList()) _cableRows.Remove(gone);
        var rows = found.Select(f =>
        {
            if (!_cableRows.TryGetValue(f.Id, out var row)) _cableRows[f.Id] = row = new CableRow(f.Id, AllowCable, r => _mixer.UnmarkCable(r.Id));
            SyncCableRow(row, f);
            return row;
        }).ToList();
        if (!rows.SequenceEqual(WiringCables.Items))
        {
            WiringCables.Items.Clear();
            foreach (var row in rows) WiringCables.Items.Add(row);
        }
        WiringCables.Summary = $"{found.Count(f => f.IsPair)} found";
        var install = _modes.Current is { } support && !SoundModes.CableInstalled(support) ? CableSetupAction() : null;
        if (install?.Label != WiringCables.Install?.Label) WiringCables.Install = install;
    }

    private void SyncCableRow(CableRow row, FoundCable f)
    {
        var allowed = _mixer.IsCableAllowed(f.Id);
        row.Name = f.Name;
        row.Tip = f.Marked ? "A pair you marked as a cable" : f.Kind switch
        {
            CableKind.Mixer => $"A virtual mixer by {f.Maker}: sound sent there goes into its mixer",
            CableKind.VoiceChanger => $"A voice changer by {f.Maker}",
            _ => $"A virtual cable by {f.Maker}",
        };
        row.EndsTag = f.Ends switch
        {
            CableEnds.Both => "Input + Output",
            CableEnds.InputOnly => "Input only",
            CableEnds.OutputOnly => "Output only",
            _ => "Not plugged in",
        };
        row.CanSwitch = f.IsPair;
        row.CanRemove = f.Marked;
        row.LoadAllowed(f.IsPair && allowed);

        var why = NotUsedWhy(f, allowed);
        row.IsQuiet = why is not null;
        if (why is not null)
        {
            row.State = string.Empty;
            row.Use = NotUsedPrefix + why;
            row.UseTip = f.IsPair ? "Switch it on to let OmniControl use it" : "Only a cable with both ends, an input and the output it plays into, can be used";
            return;
        }
        var holder = CableHolderOf(f);
        var inUse = holder is not null;
        row.State = inUse ? InUseState : FreeState;
        row.IsFree = !inUse;
        row.SwitchTip = SoundModes.CableSwitchTip(holder);
        row.Use = UsePrefix + (holder ?? AutomaticUse);
        row.UseTip = inUse ? "What the effects engine runs through it now" : "Given out when a combined device, a processed microphone or a program needs a cable";
    }

    /// <summary>Why OmniControl does not use this device, or null when it may.</summary>
    private static string? NotUsedWhy(FoundCable f, bool allowed) => f switch
    {
        { Kind: CableKind.Mixer } => "a mixer, not a cable",
        { Kind: CableKind.VoiceChanger } => "a voice changer, not a cable",
        { Ends: CableEnds.InputOnly } => "no matching output",
        { Ends: CableEnds.OutputOnly } => "no matching input",
        { Ends: CableEnds.Missing } => "not plugged in",
        _ => allowed ? null : "switched off",
    };

    /// <summary>
    /// What holds this cable now, in words: "master bus", the row that runs through it, or "kept for its device" (a combined device's
    /// cable); null = it is free.
    /// </summary>
    private string? CableHolderOf(FoundCable f)
    {
        var inputs = f.Inputs.Select(c => c.PlaybackId).ToList();
        var user = _engine.RowOnCable(inputs);
        return user == MixerService.MasterKey ? MasterBusUse
            : user is not null ? RowOf(user)?.Name ?? user
            : inputs.Any(_engine.CablesInUse.Contains) ? HeldUse
            : null;
    }

    private void AllowCable(CableRow row, bool allowed)
    {
        var holder = !allowed && _modes.Current?.Found.FirstOrDefault(f => f.Id == row.Id) is { } found ? CableHolderOf(found) : null;
        _mixer.SetCableAllowed(row.Id, allowed);
        _report(allowed ? $"{row.Name} can be used"
            : holder is not null ? SoundModes.CableSwitchedOffStops(row.Name, holder)
            : $"{row.Name} switched off: OmniControl won't use it");
    }

    /// <summary>The pop-down opened: every plugged-in device that is not a known cable end.</summary>
    private void OpenCableMark(CableMarkMenu menu)
    {
        var ends = (_modes.Current?.Found ?? Array.Empty<FoundCable>())
            .SelectMany(f => f.Inputs.Select(c => c.PlaybackId).Append(f.Id)).ToHashSet();
        bool Unknown(AudioDevice d) => !VirtualCables.IsCable(d.Name) && !ends.Contains(d.Id);
        menu.Refresh(_mixer.Devices.Where(Unknown).ToList(), _mixer.RecordingDevices.Where(Unknown).ToList());
    }

    private void MarkCable(string name, string playbackId, string recordingId)
    {
        _mixer.MarkCable(name, playbackId, recordingId);
        _report($"{name} marked as a virtual cable");
    }

    private IEnumerable<SearchEntry> CableEntries() =>
        WiringCables.Items.Select(c => Entry(c, WiringCables.Name, CablesKeywords, WiringTab));
}

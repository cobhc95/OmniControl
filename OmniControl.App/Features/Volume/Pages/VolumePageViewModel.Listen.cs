using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Volume.Pages;

// A microphone row's Listen button: Windows' own "Listen to this device", so Windows plays the input itself
// and OmniControl adds no delay. The state is what Windows reports (read on every scan and after each change); the right-click menu picks
// the output. The state and its On / Off / Toggle actions act on the default microphone, like Mic mute.
public sealed partial class VolumePageViewModel
{
    private const string MicListenId = VolumeStates.MicListenId;
    private static readonly OnOffFamily MicListenFamily = new("volume.mic.listen", "Mic listen", MicListenId);
    private const string DefaultOutputWords = "Default speaker";
    private string? _listenFailed;

    /// Reads Windows' Listen setting of one microphone into its row.
    private void SyncListen(InputItem item)
    {
        if (!CoreAudio.GetListen(item.Id, out var on, out var output)) return;
        item.SyncListen(on, OutputName(output));
    }

    private string OutputName(string? outputId) =>
        outputId is null
            ? $"{DefaultOutputWords} · {_mixer.Devices.FirstOrDefault(d => d.IsDefault)?.Name ?? "none"}"
            : _mixer.Devices.FirstOrDefault(d => d.Id == outputId)?.Name ?? "an output that is not connected";

    /// Turns listening on or off, keeping the output (<paramref name="keepOutput"/>) or setting a new one; says once when Windows refuses.
    private void SetListen(InputItem item, bool on, string? outputId, bool keepOutput)
    {
        if (keepOutput) CoreAudio.GetListen(item.Id, out _, out outputId);
        if (!CoreAudio.TrySetListen(item.Id, on, outputId, out var error))
        {
            if (_listenFailed != error)
            {
                _report($"Windows did not change Listen for {item.Name}.");
                Shared.Services.Guard.Log("Mic listen", new InvalidOperationException(error));
            }
            _listenFailed = error;
        }
        else _listenFailed = null;
        SyncListen(item);
        _states.Notify(MicListenId);
    }

    /// The Listen button's menu: the default output first, then each output; the ticked one is where it plays now.
    private IReadOnlyList<MenuRowItem> ListenRows(InputItem item)
    {
        CoreAudio.GetListen(item.Id, out var on, out var current);
        var rows = new List<MenuRowItem> { MenuRowItem.Header("Listen on") };
        rows.Add(new MenuRowItem(OutputName(null), string.Empty, current is null, string.Empty, new RelayCommand(() => SetListen(item, on, null, false))));
        foreach (var d in _mixer.Devices)
            rows.Add(new MenuRowItem(d.Name, string.Empty, current == d.Id, string.Empty, new RelayCommand(() => SetListen(item, on, d.Id, false))));
        return rows;
    }

    private void SetDefaultListen(bool? on) => Dispatcher.UIThread.Post(() =>
    {
        if (DefaultInput() is { } input) SetListen(input, on ?? !input.Listening, null, keepOutput: true);
    });

    private void RegisterListen(IActionRegistry hotkeys)
    {
        var toggle = new HotkeyAction(MicListenFamily.Id, "Toggle mic listen", Title, "sound audio input microphone mic listen monitor hear myself playback",
            _ => SetDefaultListen(null)) { Icon = "headphones", Family = MicListenFamily.Family, FamilyRole = OnOffFamily.ToggleRole };
        hotkeys.Register(MicListenFamily.Siblings(toggle, on => SetDefaultListen(on)).Prepend(toggle));
        _states.Register(new[]
        {
            new StateEntry(MicListenId, "Mic listen", Title, StateKind.OnOff,
                () => DefaultInput() is { } input ? StateValue.Of(input.Listening, DateTime.Now, VolumeStates.DeviceSubject("Default microphone", input.Name)) : null,
                "Windows' own \"Listen to this device\" of the default microphone, read from Windows on every scan.",
                Keywords: "sound audio input microphone mic listen monitor hear", Short: "Listen", Icon: "headphones",
                SuggestedIcons: new[] { "headphones", MicIcon }, Set: value => SetDefaultListen(VolumeStates.IsOn(value))),
        });
    }
}

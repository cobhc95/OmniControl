using System.IO.Pipes;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace OmniControl.Core.Engine.Apo;

/// Checks that Equalizer APO really started on devices just attached, as its Device Selector's check does (DeviceTestThread.cpp,
/// ReceiveThread.cpp and DeviceAPOInfo::testAPOInstallation of Equalizer APO 1.4.2, GPL-2.0-or-later, Copyright (C) 2024 Jonas Thedering):
/// a pipe is named in Equalizer APO's key, a shared stream is opened on each device so Windows starts its effects, and every APO that
/// starts there writes {"deviceGuid", "stage", "phase"} to the pipe. Run after the audio restart, elevated. Unlike Device Selector it does
/// not try the other install modes by itself (each try is another audio restart); the device's Advanced menu picks a mode.
internal static class ApoDeviceTest
{
    private const string PipeName = "OmniControlApoDeviceTest";

    /// The devices among <paramref name="devices"/> on which every APO that should start did (and its child APO, when one should run inside it).
    /// Disabled and unplugged devices are not checked (they count as passed, as in Device Selector).
    public static IReadOnlySet<string> Passed(IApoRegistry reg, IReadOnlyList<(DeviceApo Device, ApoInstallState State)> devices)
    {
        var passed = devices.Where(d => d.Device.Disabled || d.Device.Unplugged).Select(d => d.Device.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var waiting = devices.Where(d => !passed.Contains(d.Device.Guid)).ToDictionary(d => d.Device.Guid, d => new Result(d.Device, d.State), StringComparer.OrdinalIgnoreCase);
        if (waiting.Count == 0) return passed;

        using var stop = new CancellationTokenSource();
        reg.WriteString(ApoIds.UpstreamAppKey, ApoIds.DeviceTestPipeValue, PipeName);
        try
        {
            var listening = Task.Run(() => Listen(waiting, stop.Token));
            foreach (var result in waiting.Values) Open(result.Device);
            stop.CancelAfter(CoreTiming.ApoCheckWaitMs);
            listening.Wait();
        }
        finally
        {
            reg.DeleteValue(ApoIds.UpstreamAppKey, ApoIds.DeviceTestPipeValue);
        }
        passed.UnionWith(waiting.Values.Where(r => r.Done).Select(r => r.Device.Guid));
        return passed;
    }

    /// What one device has said so far (DeviceTestThread's TestResult).
    private sealed class Result
    {
        private readonly ApoInstallState _state;
        public Result(DeviceApo device, ApoInstallState state)
        {
            (Device, _state) = (device, state);
            ChildPre = !state.OriginalPreMix || device.OriginalPreMix(state.Mode).Length == 0;
            ChildPost = !state.OriginalPostMix || device.OriginalPostMix(state.Mode).Length == 0;
        }

        public DeviceApo Device { get; }
        public bool Pre, Post, ChildPre, ChildPost;

        public bool Done => (Pre && ChildPre || !_state.PreMix) && (Post && ChildPost || !_state.PostMix || Device.Input);
    }

    private static void Listen(Dictionary<string, Result> waiting, CancellationToken stop)
    {
        // No access list, as Device Selector's pipe: audiodg's restricted token must be able to write; local only, and it lives a few seconds.
        var security = new PipeSecurity();
        security.SetSecurityDescriptorSddlForm("D:NO_ACCESS_CONTROL");
        var buffer = new byte[1024];
        while (!stop.IsCancellationRequested && waiting.Values.Any(r => !r.Done))
        {
            using var pipe = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Message, PipeOptions.Asynchronous, 0, buffer.Length, security);
            try
            {
                pipe.WaitForConnectionAsync(stop).GetAwaiter().GetResult();
                var read = pipe.ReadAsync(buffer, stop).AsTask().GetAwaiter().GetResult();
                Take(waiting, Encoding.UTF8.GetString(buffer, 0, read));
            }
            catch (OperationCanceledException)
            {
                return; // the wait is over: what has not answered failed
            }
            catch (IOException)
            {
                // the APO closed its end early; the next one connects to a new instance
            }
        }
    }

    private static void Take(Dictionary<string, Result> waiting, string message)
    {
        try
        {
            using var json = JsonDocument.Parse(message);
            var root = json.RootElement;
            if (!waiting.TryGetValue(root.GetProperty("deviceGuid").GetString() ?? string.Empty, out var result)) return;
            var pre = root.GetProperty("stage").GetString() == "PreMix";
            var initialize = root.GetProperty("phase").GetString() == "Initialize";
            if (pre && initialize) result.Pre = true;
            else if (pre) result.ChildPre = true;
            else if (initialize) result.Post = true;
            else result.ChildPost = true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // not a message of Equalizer APO's device check: ignored
        }
    }

    /// DeviceAPOInfo::testAPOInstallation: a 100 ms shared stream in the device's own format makes Windows start its effects.
    private static void Open(DeviceApo device)
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var endpoint = devices.GetDevice((device.Input ? "{0.0.1.00000000}." : "{0.0.0.00000000}.") + device.Guid);
            if (endpoint.State != NAudio.CoreAudioApi.DeviceState.Active) return;
            using var client = endpoint.CreateAudioClient();
            client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.None, 1_000_000, 0, client.MixFormat, Guid.Empty);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidCastException)
        {
            // the device refused the stream: its APOs never start, so it fails the check
        }
    }
}

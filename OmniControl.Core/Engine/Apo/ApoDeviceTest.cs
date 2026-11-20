using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace OmniControl.Core.Engine.Apo;

/// How the check of the devices Equalizer APO is on ended (<see cref="ApoDeviceTest.Run"/>).
/// <param name="Checks">Each checked device's result (<see cref="ApoSetup.Running"/>, <see cref="ApoSetup.OriginalFailed"/>,
/// <see cref="ApoSetup.Failed"/>) by its key name; disabled and unplugged devices are not checked, so they are not here.</param>
/// <param name="RestartFailed">Why Windows audio did not restart (the check then stopped), or null.</param>
internal sealed record ApoCheckResult(IReadOnlyDictionary<string, string> Checks, string? RestartFailed);

/// Equalizer APO's Device Selector check, line for line (DeviceTestDialog.cpp, DeviceTestThread.cpp, ReceiveThread.cpp and
/// DeviceAPOInfo::testAPOInstallation of Equalizer APO 1.4.2, GPL-2.0-or-later, Copyright (C) 2024 Jonas Thedering): Windows audio
/// restarts, then every device Equalizer APO is on (disabled and unplugged ones excepted) gets a shared stream so Windows starts its
/// effects, and each APO that starts reports {"deviceGuid", "stage", "phase"} to the pipe named in Equalizer APO's key. A device that does
/// not answer within 3 s is put on again in the next install mode (only its chosen one when "Auto" is off) and Windows audio restarts again;
/// when none is left it stays in the mode that did best. Run elevated.
internal static class ApoDeviceTest
{
    private const string PipeName = "EqualizerAPODeviceTest";

    public static ApoCheckResult Run(IApoRegistry reg, Action<string, string> saveBackup)
    {
        var infos = DeviceApo.LoadAll(reg, input: false).Concat(DeviceApo.LoadAll(reg, input: true))
            .Where(d => d.Installed && !d.Disabled && !d.Unplugged)
            .ToDictionary(d => d.Guid.ToLowerInvariant(), d => new TestInfo(d));
        var checks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (AudioService.Restart() is { } why) return new ApoCheckResult(checks, why);

        var remaining = infos.Keys.ToHashSet();
        while (remaining.Count > 0)
        {
            using var receiver = new Receiver();
            reg.WriteString(ApoIds.UpstreamAppKey, ApoIds.DeviceTestPipeValue, PipeName);
            try
            {
                foreach (var guid in remaining)
                {
                    var info = infos[guid];
                    info.Remaining.Remove(info.Selected.Mode);
                    info.Current.ChildPre = !info.Selected.OriginalPreMix || info.Device.OriginalPreMix(info.Selected.Mode).Length == 0;
                    info.Current.ChildPost = !info.Selected.OriginalPostMix || info.Device.OriginalPostMix(info.Selected.Mode).Length == 0;
                    Open(info.Device);
                }

                var until = DateTime.UtcNow.AddMilliseconds(CoreTiming.ApoCheckWaitMs);
                while (receiver.Next(until) is { } message)
                {
                    // Device Selector logs a message it can't read, or one from a device it isn't checking, and ends its check there.
                    if (Parse(message) is not { } parsed || !infos.TryGetValue(parsed.DeviceGuid.ToLowerInvariant(), out var info))
                        return new ApoCheckResult(checks, null);
                    var (deviceGuid, stage, phase) = parsed;
                    var result = info.Current;
                    if (stage == "PreMix")
                    {
                        if (phase == "Initialize") result.Pre = true;
                        else if (phase == "ChildAPO") result.ChildPre = true;
                    }
                    else if (stage == "PostMix")
                    {
                        if (phase == "Initialize") result.Post = true;
                        else if (phase == "ChildAPO") result.ChildPost = true;
                    }
                    if ((result.Pre && result.ChildPre || !info.Selected.PreMix) && (result.Post && result.ChildPost || !info.Selected.PostMix || info.Device.Input))
                    {
                        if (remaining.Remove(deviceGuid.ToLowerInvariant())) checks[info.Device.Guid] = ApoSetup.Running;
                        if (remaining.Count == 0) break;
                    }
                }

                if (remaining.Count == 0) continue;
                foreach (var guid in remaining.ToList())
                {
                    var info = infos[guid];
                    if (info.Current.Score > info.Best.Score)
                    {
                        info.BestMode = info.Selected.Mode;
                        info.Best = info.Current;
                    }
                    ApoInstallMode mode;
                    TestResult shown;
                    if (info.Remaining.Count > 0)
                    {
                        mode = info.Remaining[0];
                        shown = info.Current;
                        info.Current = new TestResult();
                    }
                    else
                    {
                        mode = info.BestMode;
                        shown = info.Best;
                        remaining.Remove(guid);
                        checks[info.Device.Guid] = CheckOf(shown, info);
                    }
                    info.Selected = info.Selected with
                    {
                        Mode = mode,
                        OriginalPreMix = info.WantsOriginalPreMix && info.Device.OriginalPreMix(mode).Length > 0,
                        OriginalPostMix = info.WantsOriginalPostMix && info.Device.OriginalPostMix(mode).Length > 0,
                    };
                    info.Device = info.Device.Reinstall(reg, info.Selected, saveBackup);
                }
                if (AudioService.Restart() is { } failed) return new ApoCheckResult(checks, failed);
            }
            finally
            {
                reg.DeleteValue(ApoIds.UpstreamAppKey, ApoIds.DeviceTestPipeValue);
            }
        }
        return new ApoCheckResult(checks, null);
    }

    /// The icons Device Selector's check shows for a device it gave up on: any red cross is a failure, a warning sign alone means Equalizer
    /// APO runs but the driver's own APO did not start inside it.
    private static string CheckOf(TestResult r, TestInfo info)
    {
        var s = info.Selected;
        var preFails = s.PreMix && !r.Pre;
        var postFails = s.PostMix && !info.Device.Input && !r.Post;
        if (preFails || postFails) return ApoSetup.Failed;
        var childFails = s.PreMix && !r.ChildPre && s.OriginalPreMix || s.PostMix && !info.Device.Input && !r.ChildPost && s.OriginalPostMix;
        return childFails ? ApoSetup.OriginalFailed : ApoSetup.Running;
    }

    private static (string DeviceGuid, string Stage, string Phase)? Parse(string message)
    {
        try
        {
            using var json = JsonDocument.Parse(message);
            var root = json.RootElement;
            string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
            return (Text("deviceGuid"), Text("stage"), Text("phase"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// What one try on one device brought (DeviceTestThread's TestResult).
    private sealed class TestResult
    {
        public bool Pre, Post, ChildPre = true, ChildPost = true;

        public int Score => (Pre ? 20 : 0) + (Post ? 10 : 0) - (ChildPre ? 0 : 2) - (ChildPost ? 0 : 1);
    }

    /// One device under check (DeviceTestThread's DeviceTestInfo): the modes still to try, the best so far.
    private sealed class TestInfo
    {
        public TestInfo(DeviceApo device)
        {
            Device = device;
            Selected = device.Current;
            Remaining = Selected.AutoAdjust ? new List<ApoInstallMode> { ApoInstallMode.SfxEfx, ApoInstallMode.SfxMfx, ApoInstallMode.LfxGfx } : new List<ApoInstallMode> { Selected.Mode };
            BestMode = Selected.Mode;
            WantsOriginalPreMix = Selected.OriginalPreMix || device.OriginalPreMix(Selected.Mode).Length == 0;
            WantsOriginalPostMix = Selected.OriginalPostMix || device.OriginalPostMix(Selected.Mode).Length == 0;
        }

        public DeviceApo Device;
        public ApoInstallState Selected;
        public readonly List<ApoInstallMode> Remaining;
        public ApoInstallMode BestMode;
        public TestResult Current = new(), Best = new();
        public readonly bool WantsOriginalPreMix, WantsOriginalPostMix;
    }

    /// ReceiveThread: one pipe instance after another, no access list (audiodg's restricted token must be able to write; local only, it lives
    /// a few seconds), each message queued for <see cref="Next"/>.
    private sealed class Receiver : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly BlockingCollection<string> _messages = new();
        private readonly Task _loop;

        public Receiver() => _loop = Task.Run(Loop);

        /// The next message, or null when none came before <paramref name="until"/> (UTC).
        public string? Next(DateTime until)
        {
            var wait = until - DateTime.UtcNow;
            return wait > TimeSpan.Zero && _messages.TryTake(out var message, wait) ? message : null;
        }

        private void Loop()
        {
            var security = new PipeSecurity();
            security.SetSecurityDescriptorSddlForm("D:NO_ACCESS_CONTROL");
            var buffer = new byte[1024];
            while (!_stop.IsCancellationRequested)
            {
                using var pipe = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message, PipeOptions.Asynchronous, buffer.Length, 0, security);
                try
                {
                    pipe.WaitForConnectionAsync(_stop.Token).GetAwaiter().GetResult();
                    var read = pipe.ReadAsync(buffer, _stop.Token).AsTask().GetAwaiter().GetResult();
                    if (read > 0) _messages.Add(Encoding.UTF8.GetString(buffer, 0, read));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // the APO closed its end early; the next one connects to a new instance
                }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _loop.Wait();
            _stop.Dispose();
            _messages.Dispose();
        }
    }

    /// DeviceAPOInfo::testAPOInstallation: a 100 ms shared stream in the device's mix format makes Windows start its effects; a disabled or
    /// unplugged device is skipped.
    private static void Open(DeviceApo device)
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var endpoint = devices.GetDevice((device.Input ? "{0.0.1.00000000}." : "{0.0.0.00000000}.") + device.Guid);
            if ((endpoint.State & (NAudio.CoreAudioApi.DeviceState.Disabled | NAudio.CoreAudioApi.DeviceState.Unplugged)) != 0) return;
            using var client = endpoint.CreateAudioClient();
            client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.None, 1_000_000, 0, client.MixFormat, Guid.Empty);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidCastException)
        {
            // Device Selector shows the failed call in a message box and goes on: no APO starts there, so the device fails the check
        }
    }
}

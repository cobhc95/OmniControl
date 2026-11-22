# PluginHarness

One-off check of VST2 and VST3 plug-ins through OmniControl's own host (`PluginLoader.Load`, 48 kHz, Core's block limit, 512-frame blocks).
Not in `OmniControl.slnx`, the build scripts or CI.

    $env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
    dotnet build tools\PluginHarness -c Release
    tools\PluginHarness\bin\Release\net10.0-windows10.0.19041.0\PluginHarness.exe "C:\Program Files\VSTPlugins" "C:\Program Files\Common Files\VST3"

Per plug-in (each in its own child process, so a native crash is one row with its exit code; 90 s limit): load, channels, parameters, chunks
(VST2), latency, 2 s of pink noise (-18 dBFS) plus a 1 kHz burst; silent / same as input (null test), 1/3-octave spectrum change; then
all parameters at 0.75 (else the first gain/drive/mix-like parameter at 0 and 1) and whether that is audible (a band moving more than 0.1 dB);
then the saved state restored into a fresh instance and compared with the changed one. Writes `harness-results.csv` next to the exe.

Limits: run from a non-elevated shell (an elevated host refuses plug-ins in user-writable folders, like the app); the test is mono-in-both-channels
and reads the first output channel; the host takes only stereo/mono, instruments are refused. Editors are not opened (a plug-in that only
reacts to its editor shows as unchanged).

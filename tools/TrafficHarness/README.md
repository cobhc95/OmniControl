# TrafficHarness

A one-off check of per-program limits. Not part of OmniControl.slnx, the build scripts or CI.

It measures its own speed against Cloudflare's speed test (download 20 MB, upload 10 MB, over TCP and HTTP/3 where .NET supports QUIC), sets a 256 KB/s download limit and a 128 KB/s upload limit on itself through OmniControl's own engine (`NetworkWork`: Windows throttle rules for upload, Windows Packet Filter for download), measures 20 s per case again and prints a table. The parallel case runs two connections in the harness and one in each of two child processes of the same program, so it passes only when the whole program shares one limit. Every rule it set is taken out at the end, also on Ctrl+C.

## Run

1. Close OmniControl and wait about a minute for its network service to stop by itself (the harness refuses while any OmniControl process runs).
2. From an administrator PowerShell in the repository folder:

   ```
   dotnet run -c Release --project tools\TrafficHarness
   ```

Needs administrator rights and, for the download cases, Windows Packet Filter installed (OmniControl's Setup installs it); without it those cases say "skipped". Exit code 0 = all pass, 1 = a case failed, 2 = refused.

Taking its rules out removes every rule named "OmniControl ..." from Windows; OmniControl puts its own back when it next starts.

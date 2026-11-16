# Builds the Setup and Portable downloads for the version in OmniControl.App.csproj and publishes them as a GitHub pre-release.
# Usage: powershell -File tools\release.ps1   (bump <Version> and CHANGELOG.md first; needs dotnet, Inno Setup 6 and gh)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'OmniControl.App\OmniControl.App.csproj'
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$pub = Join-Path $root "bin\release\$version\publish"
$out = Join-Path $root "bin\release\$version\out"
Remove-Item -Recurse -Force (Split-Path $pub) -ErrorAction SilentlyContinue
dotnet publish $proj -c Release -r win-x64 --self-contained true -o $pub --nologo -v q
if ($LASTEXITCODE) { throw 'publish failed' }
New-Item -ItemType Directory -Force $out | Out-Null
Compress-Archive -Path "$pub\*" -DestinationPath (Join-Path $out "OmniControl-$version-Portable.zip") -Force
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
& $iscc /Q "/DAppVersion=$version" "/DPubDir=$pub" "/DOutDir=$out" "/DRoot=$root" (Join-Path $PSScriptRoot 'setup.iss')
if ($LASTEXITCODE) { throw 'setup build failed' }
$log = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
$notes = [regex]::Match($log, "(?ms)^## $([regex]::Escape($version))[^\n]*\n(.*?)(?=^## |\z)").Groups[1].Value.Trim()
$files = Get-ChildItem $out -File | Where-Object { $_.Name -match 'Setup\.exe$|Portable\.zip$' } | ForEach-Object FullName
gh release create "v$version" @files --prerelease --title "OmniControl $version" --notes $notes

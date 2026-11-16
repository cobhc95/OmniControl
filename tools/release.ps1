# Builds the Setup and Portable downloads for the version in OmniControl.App.csproj and publishes them as a GitHub pre-release.
# Usage: powershell -File tools\release.ps1   (bump <Version> and CHANGELOG.md first; needs dotnet, vpk and gh)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'OmniControl.App\OmniControl.App.csproj'
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$pub = Join-Path $root "bin\release\$version\publish"
$out = Join-Path $root "bin\release\$version\out"
Remove-Item -Recurse -Force (Split-Path $pub) -ErrorAction SilentlyContinue
dotnet publish $proj -c Release -r win-x64 --self-contained true -o $pub --nologo -v q
if ($LASTEXITCODE) { throw 'publish failed' }
vpk pack --packId OmniControl --packVersion $version --packDir $pub --mainExe OmniControl.exe --packTitle OmniControl --icon (Join-Path $root 'OmniControl.App\Assets\OmniControl.ico') --outputDir $out
if ($LASTEXITCODE) { throw 'pack failed' }
$log = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
$notes = [regex]::Match($log, "(?ms)^## $([regex]::Escape($version))[^\n]*\n(.*?)(?=^## |\z)").Groups[1].Value.Trim()
$files = Get-ChildItem $out -File | Where-Object { $_.Name -match 'Setup\.exe$|Portable\.zip$' } | ForEach-Object FullName
gh release create "v$version" @files --prerelease --title "OmniControl $version" --notes $notes

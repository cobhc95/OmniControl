# Release helper for the version in OmniControl.App.csproj (bump <Version> and CHANGELOG.md first).
#   release.ps1             check the tree is clean and pushed, create and push the annotated tag v<version>; the GitHub workflow then builds and publishes
#   release.ps1 -BuildOnly  build Portable, Setup, SHA256SUMS and notes into bin\release\<version>\out (the workflow's build step; needs dotnet and Inno Setup 6)
#   release.ps1 -Local      -BuildOnly, then publish the GitHub release from this PC (needs gh); not attested
param([switch]$BuildOnly, [switch]$Local)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'OmniControl.App\OmniControl.App.csproj'
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$out = Join-Path $root "bin\release\$version\out"

function Invoke-Git {
    $result = & git -C $root @args
    if ($LASTEXITCODE) { throw "git $args failed" }
    $result
}

function Publish-Tag {
    if (Invoke-Git status --porcelain) { throw 'The working tree has uncommitted changes.' }
    Invoke-Git fetch origin --tags --quiet
    if (Invoke-Git rev-list '@{u}..HEAD') { throw 'HEAD is not pushed; push the branch first.' }
    if (Invoke-Git tag --list "v$version") { throw "Tag v$version already exists." }
    Invoke-Git tag -a "v$version" -m "OmniControl $version" HEAD
    Invoke-Git push origin "v$version"
    "Tag v$version pushed; the Release workflow builds and publishes it."
}

. (Join-Path $PSScriptRoot 'OptionalFileNames.ps1')

# The published app must start whole and with Setup's optional files left out (--check-start starts nothing else).
function Test-Start([string]$folder) {
    $run = Start-Process -FilePath (Join-Path $folder 'OmniControl.exe') -ArgumentList '--check-start' -Wait -PassThru
    if ($run.ExitCode) { throw "The published app did not start from $folder (exit code $($run.ExitCode))." }
}

function Test-PublishedStart([string]$pub) {
    Test-Start $pub
    $trimmed = Join-Path (Split-Path $pub) 'publish-trimmed'
    Copy-Item $pub $trimmed -Recurse
    try {
        Get-OptionalFileNames (Join-Path $root 'OmniControl.Core\Engine\OptionalFiles.cs') | ForEach-Object { Remove-Item (Join-Path $trimmed $_) -Force -ErrorAction SilentlyContinue }
        Test-Start $trimmed
    }
    finally { Remove-Item $trimmed -Recurse -Force }
}

function Build-Release {
    $pub = Join-Path $root "bin\release\$version\publish"
    Remove-Item -Recurse -Force (Split-Path $pub) -ErrorAction SilentlyContinue
    dotnet publish $proj -c Release -r win-x64 --self-contained true -o $pub --nologo -v q
    if ($LASTEXITCODE) { throw 'publish failed' }
    Test-PublishedStart $pub
    New-Item -ItemType Directory -Force $out | Out-Null
    Compress-Archive -Path "$pub\*" -DestinationPath (Join-Path $out "OmniControl-$version-Portable.zip") -Force
    $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 is not installed.' }
    & $iscc /Q "/DAppVersion=$version" "/DPubDir=$pub" "/DOutDir=$out" "/DRoot=$root" (Join-Path $PSScriptRoot 'setup.iss')
    if ($LASTEXITCODE) { throw 'setup build failed' }
    $log = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
    $notes = [regex]::Match($log, "(?ms)^## $([regex]::Escape($version))[^\n]*\n(.*?)(?=^## |\z)").Groups[1].Value.Trim()
    if (-not $notes) { throw "CHANGELOG.md has no section for $version." }
    Set-Content -Path (Join-Path $out 'notes.md') -Value $notes -Encoding utf8
    # The downloads are not code-signed (no certificate): their SHA-256 sums go up beside them, in the format sha256sum -c reads.
    $files = @(Get-ChildItem $out -File | Where-Object { $_.Name -match 'Setup\.exe$|Portable\.zip$' })
    $files | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
        Set-Content -Path (Join-Path $out "OmniControl-$version-SHA256SUMS.txt") -Encoding ascii
}

if (-not ($BuildOnly -or $Local)) { Publish-Tag; return }
Build-Release
if ($Local) {
    $files = @(Get-ChildItem $out -File | Where-Object { $_.Name -match 'Setup\.exe$|Portable\.zip$|SHA256SUMS\.txt$' } | ForEach-Object FullName)
    gh release create "v$version" @files --latest --title "OmniControl $version" --notes-file (Join-Path $out 'notes.md')
}

# OmniControl APO build script (x64 Release only). Run by the owner in a normal PowerShell window, NOT as administrator:
#
#   powershell -ExecutionPolicy Bypass -File native\EqualizerAPO\build.ps1
#
# It needs: Visual Studio 2022 or Build Tools 2022 with "Desktop development with C++" (MSVC v143) and the Windows 11 SDK
# (10.0.26100 has audioeng.lib, AudioBaseProcessingObject.lib, audiomediatypecrt.lib, wmcodecdspuuid.lib) and an internet
# connection. It downloads the pinned third-party sources/binaries listed in README.md into .\deps (each in its own
# folder, licence files kept), builds Common.lib and OmniControlAPO.dll, and copies the DLL and the two runtime DLLs it needs into
# ..\..\OmniControl.App\apo\ so the app build copies them next to OmniControl.exe.
#
# It installs nothing, registers nothing and changes no system setting. Installing is done from OmniControl (Volume page, master
# effects mode "Equalizer APO", Install), which shows the warning and a preview first.

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$deps = Join-Path $here 'deps'
$out = Join-Path $here 'build-x64'
$appApo = Join-Path $here '..\..\OmniControl.App\apo'

# Pinned versions and hashes (see README.md "Dependencies"). A download whose SHA-256 differs stops the build.
$libsndfileZip = 'https://github.com/libsndfile/libsndfile/releases/download/1.2.2/libsndfile-1.2.2-win64.zip'
$fftwZip = 'https://fftw.org/pub/fftw/fftw-3.3.5-dll64.zip'
# muparserx 3.0.1 is no longer published by its author; Equalizer APO's developer page attaches the exact source.
$muparserxZip = 'https://sourceforge.net/p/equalizerapo/wiki/Developer%20documentation/attachment/muparserx_v3_0_1_vs_2019.zip'
# SHA-256 of the files as downloaded on 12-13 Nov 2026 from the URLs above.
$expectedHashes = @{
    'libsndfile.zip' = '2173935C0C1ED13CF627951D34483F9D405EAD2EB473190461C42BA220643A3F'
    'fftw.zip'       = 'CFD88DC0E8D7001115EA79E069A2C695D52C8947F5B4F3B7AC54A192756F439F'
    'muparserx.zip'  = '05627A52DE2D0439953FA54A1F025D250E9E9E18F13B1CB9220F25F5BBE1DE15'
}

function Get-Download($url, $name) {
    $file = Join-Path $deps $name
    if (-not (Test-Path $file)) { Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing }
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash
    $want = $expectedHashes[$name]
    if ($want -and $want -ne $hash) { throw "$name has SHA-256 $hash, expected $want. Delete it and check the source." }
    Write-Host "$name SHA-256 $hash"
    return $file
}

New-Item -ItemType Directory -Force $deps, $out, $appApo | Out-Null

# Visual Studio's developer tools (lib.exe, msbuild).
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio 2022 (or Build Tools) with the C++ workload is not installed.' }
$msbuild = Join-Path $vs 'MSBuild\Current\Bin\MSBuild.exe'
$libExe = Get-ChildItem (Join-Path $vs 'VC\Tools\MSVC') -Recurse -Filter lib.exe | Where-Object FullName -match 'Hostx64\\x64' | Select-Object -First 1

# libsndfile 1.2.2 (LGPL-2.1): prebuilt official Windows x64 package.
$sndZip = Get-Download $libsndfileZip 'libsndfile.zip'
$sndDir = Join-Path $deps 'libsndfile'
if (-not (Test-Path $sndDir)) {
    Expand-Archive $sndZip -DestinationPath (Join-Path $deps 'libsndfile-unpacked')
    Move-Item (Get-ChildItem (Join-Path $deps 'libsndfile-unpacked') -Directory | Select-Object -First 1).FullName $sndDir
}

# FFTW 3.3.5 (GPL-2.0-or-later): official Windows 64-bit DLLs; the import library is made with lib.exe as FFTW's page says.
$fftwFile = Get-Download $fftwZip 'fftw.zip'
$fftwDir = Join-Path $deps 'fftw'
if (-not (Test-Path $fftwDir)) {
    Expand-Archive $fftwFile -DestinationPath $fftwDir
    & $libExe.FullName "/def:$(Join-Path $fftwDir 'libfftw3f-3.def')" "/out:$(Join-Path $fftwDir 'fftw3f.lib')" /machine:x64
}

# muparserx 3.0.1 (BSD-2-Clause): exactly this version (3.0.2 removed a feature Equalizer APO's parser uses). Built from its own
# Visual Studio project as a static x64 library with the installed toolset.
$mupDir = Join-Path $deps 'muparserx'
if (-not (Test-Path $mupDir)) {
    $mupZip = Get-Download $muparserxZip 'muparserx.zip'
    Expand-Archive $mupZip -DestinationPath (Join-Path $deps 'muparserx-unpacked')
    Move-Item (Join-Path $deps 'muparserx-unpacked\muparserx_v3_0_1') $mupDir
    $mupProject = Join-Path $mupDir 'build\vs2019\muparserx\muparserx.vcxproj'
    & $msbuild $mupProject /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v143 /p:WindowsTargetPlatformVersion=10.0 "/p:OutDir=$(Join-Path $mupDir 'lib64')\" /m
    Get-ChildItem (Join-Path $mupDir 'lib64') -Filter '*.lib' | Where-Object Name -ne 'muparserx.lib' | Select-Object -First 1 | Copy-Item -Destination (Join-Path $mupDir 'lib64\muparserx.lib')
}

# The APO: Common.lib, then OmniControlAPO.dll (x64 Release).
$props = "/p:Configuration=Release;Platform=x64;SolutionDir=$here\;OcDeps=$deps;OutDir=$out\"
& $msbuild (Join-Path $here 'Common.vcxproj') $props /m
& $msbuild (Join-Path $here 'EqualizerAPO\EqualizerAPO.vcxproj') $props /m

Copy-Item (Join-Path $out 'OmniControlAPO.dll') $appApo -Force
Copy-Item (Join-Path $sndDir 'bin\sndfile.dll') $appApo -Force
Copy-Item (Join-Path $fftwDir 'libfftw3f-3.dll') $appApo -Force
Write-Host "Built. Files in $appApo :"
Get-ChildItem $appApo | ForEach-Object { "  $($_.Name)  SHA-256 $((Get-FileHash $_.FullName).Hash)" }

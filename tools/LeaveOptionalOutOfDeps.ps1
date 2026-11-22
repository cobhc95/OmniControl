# Takes the optional files (the list in Core/Engine/OptionalFiles.cs) out of a deps.json, so a self-contained app still starts when Setup left them out.
param([Parameter(Mandatory)][string]$List, [Parameter(Mandatory)][string]$Deps)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Deps)) { return }
. (Join-Path $PSScriptRoot 'OptionalFileNames.ps1')
$names = @(Get-OptionalFileNames $List | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) })

$json = [IO.File]::ReadAllText($Deps) | ConvertFrom-Json
$targets = @($json.targets.PSObject.Properties | ForEach-Object { $_.Value })
$gone = @($targets | ForEach-Object { $_.PSObject.Properties } | Where-Object {
        $files = @($_.Value.runtime.PSObject.Properties.Name | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) })
        $files.Count -gt 0 -and @($files | Where-Object { $names -notcontains $_ }).Count -eq 0 } | ForEach-Object { $_.Name } | Sort-Object -Unique)
if (-not $gone) { return }

foreach ($target in $targets) {
    foreach ($key in $gone) { [void]$target.PSObject.Properties.Remove($key) }
    foreach ($entry in $target.PSObject.Properties) {
        if ($entry.Value.dependencies) { foreach ($key in $gone) { [void]$entry.Value.dependencies.PSObject.Properties.Remove(($key -split '/')[0]) } }
    }
}
foreach ($key in $gone) { [void]$json.libraries.PSObject.Properties.Remove($key) }
[IO.File]::WriteAllText($Deps, ($json | ConvertTo-Json -Depth 32), (New-Object Text.UTF8Encoding $false))
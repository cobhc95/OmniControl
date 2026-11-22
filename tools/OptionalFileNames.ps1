# The optional files' names (with .dll), read from the one list in Core/Engine/OptionalFiles.cs.
function Get-OptionalFileNames([Parameter(Mandatory)][string]$List) {
    $statement = [regex]::Match([IO.File]::ReadAllText($List), 'public const string Sensors\b[^;]*;').Value
    $names = @([regex]::Matches($statement, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value -split ',' })
    if (-not $names) { throw "No optional files found in $List" }
    $names
}

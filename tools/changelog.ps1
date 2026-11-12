# Prints the CHANGELOG.md section for one version, or fails if it is missing.
#   pwsh tools/changelog.ps1 -Version 1.1.0 [-Out notes.md]
param(
    [Parameter(Mandatory)] [string]$Version,
    [string]$Out
)
$ErrorActionPreference = "Stop"

$path = Join-Path $PSScriptRoot "..\CHANGELOG.md"
$lines = Get-Content -LiteralPath $path -Encoding utf8
$header = "## [$Version]"

$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].StartsWith($header)) { $start = $i + 1; break }
}
if ($start -lt 0) {
    Write-Error "CHANGELOG.md has no '$header' section. Add the release notes before tagging v$Version."
}

$section = New-Object System.Collections.Generic.List[string]
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i].StartsWith("## [") -or $lines[$i] -match '^\[[^\]]+\]:\s') { break }
    $section.Add($lines[$i])
}

$notes = ($section -join "`n").Trim()
if (-not $notes) { Write-Error "The '$header' section in CHANGELOG.md is empty." }

if ($Out) { [IO.File]::WriteAllText($Out, $notes + "`n", (New-Object Text.UTF8Encoding $false)) }
else { $notes }

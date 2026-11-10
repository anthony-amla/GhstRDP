# Regenera os assemblies de interop do controle RDP do Windows (mstscax.dll) em lib\.
# Rodar no Windows PowerShell 5.1 (usa o AxImporter do .NET Framework):
#   powershell -ExecutionPolicy Bypass -File tools\gen-interop.ps1
$ErrorActionPreference = "Stop"
$out = Join-Path $PSScriptRoot "..\lib"
New-Item -ItemType Directory -Force $out | Out-Null

Add-Type -AssemblyName System.Design
Add-Type -AssemblyName System.Windows.Forms

$opts = New-Object System.Windows.Forms.Design.AxImporter+Options
$opts.outputDirectory = (Resolve-Path $out).Path
$opts.genSources = $false
$opts.silentMode = $true

$importer = New-Object System.Windows.Forms.Design.AxImporter($opts)
$result = $importer.GenerateFromFile((New-Object System.IO.FileInfo "$env:WINDIR\System32\mstscax.dll"))
"Gerado: $result"
$importer.GeneratedAssemblies

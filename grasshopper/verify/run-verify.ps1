# Extracts the "custom additional code" region out of GazeSessionSimulator.cs, compiles it
# against the Rhino type stubs with LangVersion 7.3, and runs the acceptance checks.
#
# Usage:  powershell -ExecutionPolicy Bypass -File run-verify.ps1 [outputCsvPath] [starVectorCount]

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceFile = Join-Path (Split-Path -Parent $here) 'GazeSessionSimulator.cs'
$generatedDir = Join-Path $here 'Generated'
$generatedFile = Join-Path $generatedDir 'SimulationCore.g.cs'

if (-not (Test-Path $generatedDir)) { New-Item -ItemType Directory -Path $generatedDir | Out-Null }

$lines = Get-Content -LiteralPath $sourceFile -Encoding UTF8

$startIndex = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Trim() -eq '#region AdditionalCodeRegion') { $startIndex = $i + 1; break }
}
if ($startIndex -lt 0) { throw 'Could not find "#region AdditionalCodeRegion" in the source file.' }

$endIndex = -1
for ($i = $lines.Count - 1; $i -gt $startIndex; $i--) {
    if ($lines[$i].Trim() -eq '#endregion') { $endIndex = $i - 1; break }
}
if ($endIndex -lt $startIndex) { throw 'Could not find the closing "#endregion" of the additional code region.' }

$body = $lines[$startIndex..$endIndex] -join "`r`n"

$header = @'
// AUTO-GENERATED from ../GazeSessionSimulator.cs - do not edit. Run run-verify.ps1 instead.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Rhino.Geometry;

namespace GazeSimulatorCore
{
'@

$content = $header + "`r`n" + $body + "`r`n}" + "`r`n"
Set-Content -LiteralPath $generatedFile -Value $content -Encoding UTF8

Write-Host "Extracted $((($body -split "`r`n").Count)) lines of component code." -ForegroundColor Cyan

$arguments = @('run', '--project', (Join-Path $here 'Verify.csproj'), '-c', 'Release', '--')
if ($args.Count -ge 1) { $arguments += $args[0] }
if ($args.Count -ge 2) { $arguments += $args[1] }

& dotnet $arguments
exit $LASTEXITCODE

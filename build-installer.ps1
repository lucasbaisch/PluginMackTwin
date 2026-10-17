<#
    Gera o instalador MSI do MackTwin.

    Fica fora da solução de propósito: o Visual Studio não carrega projetos
    .wixproj sem a extensão HeatWave (FireGiant), e um projeto que não carrega
    aparece como erro a cada abertura da solução. Pela linha de comando o
    MSBuild resolve o WiX via NuGet e compila sem extensão nenhuma.

    Uso:  .\build-installer.ps1  [-Configuration Release]
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projeto = Join-Path $PSScriptRoot 'Installer\Installer.wixproj'

Write-Host "Compilando o plug-in e empacotando o MSI ($Configuration)..." -ForegroundColor Cyan
dotnet build $projeto -c $Configuration

if ($LASTEXITCODE -ne 0) {
    throw "Falha no build (código $LASTEXITCODE)."
}

$msi = Join-Path $PSScriptRoot "Installer\bin\$Configuration\MackTwin-Setup.msi"
if (-not (Test-Path $msi)) {
    throw "Build terminou sem erro, mas o MSI não foi encontrado em $msi"
}

$tamanho = [math]::Round((Get-Item $msi).Length / 1MB, 2)
Write-Host ""
Write-Host "MSI gerado: $msi ($tamanho MB)" -ForegroundColor Green

param(
    [string]$GameDirectory = $env:STS2_INSTALL_DIR,
    [string]$DotnetPath = 'dotnet'
)
$ErrorActionPreference = 'Stop'
if (-not $GameDirectory) { throw 'Pass -GameDirectory or set STS2_INSTALL_DIR.' }
$taskGame = (Resolve-Path -LiteralPath $GameDirectory).Path
Push-Location $PSScriptRoot
try {
    & $DotnetPath build SpireMouse.csproj -c Release "-p:Sts2InstallDir=$taskGame" -p:Verify=false
    if ($LASTEXITCODE -ne 0) { throw 'Production build failed.' }
    $taskOutput = Join-Path $PSScriptRoot 'dist\SpireMouse'
    New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
    Copy-Item -LiteralPath '.godot\mono\temp\bin\Release\SpireMouse.dll','SpireMouse.json','LICENSE','LICENSE-SpeedControl.txt','docs\PROVENANCE.md' -Destination $taskOutput
    Write-Output "Production files: $taskOutput"
} finally { Pop-Location }

param(
    [Parameter(Mandatory=$true)][string]$RunName,
    [Parameter(Mandatory=$true)][string]$InstalledModDll,
    [string]$GameDirectory = $env:STS2_INSTALL_DIR,
    [string]$Suite = 'combat',
    [string]$DotnetPath = 'dotnet',
    # The stable branch (v0.111) lacks The Fool; build the verifier without those suites.
    [switch]$StableGame
)
$ErrorActionPreference = 'Stop'
if (-not $GameDirectory) { throw 'Pass -GameDirectory or set STS2_INSTALL_DIR.' }
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Use letters, digits, underscores or hyphens for RunName.' }
$taskGame = (Resolve-Path -LiteralPath $GameDirectory).Path
$taskInstalled = (Resolve-Path -LiteralPath $InstalledModDll).Path
if ((Split-Path $taskInstalled -Leaf) -ne 'SpireMouse.dll') { throw 'InstalledModDll must point to an installed SpireMouse.dll.' }
if (Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue) { throw 'Save and close the game before verification.' }
$taskOutput = Join-Path $PSScriptRoot ('artifacts\' + $RunName)
if (Test-Path -LiteralPath $taskOutput) { throw 'Use a new verification name.' }
Push-Location $PSScriptRoot
$taskPriorEnvironment = @{}
foreach ($taskName in @('SPIRE_MOUSE_VERIFY','SPIRE_MOUSE_VERIFY_SUITE','STS2_DEV_SKIP','STS2_DEV_WINDOWED','SteamAppId')) {
    $taskPriorEnvironment[$taskName] = [Environment]::GetEnvironmentVariable($taskName, 'Process')
}
$taskDllBackedUp = $false
try {
    . "$PSScriptRoot\source-fingerprint.ps1"
    New-Item -ItemType Directory -Path $taskOutput | Out-Null
    Get-SourceFingerprint | Set-Content "$taskOutput\source.sha256"
    & $DotnetPath build SpireMouse.csproj -c Release "-p:Sts2InstallDir=$taskGame" -p:Verify=true "-p:StableGame=$($StableGame.IsPresent.ToString().ToLower())" *> "$taskOutput\build.log"
    if ($LASTEXITCODE -ne 0) { throw 'Verification build failed; see build.log.' }
    Copy-Item -LiteralPath $taskInstalled -Destination "$taskOutput\previous.dll"
    $taskDllBackedUp = $true
    Copy-Item -LiteralPath '.godot\mono\temp\bin\Release\SpireMouse.dll' -Destination $taskInstalled
    $env:SPIRE_MOUSE_VERIFY = $taskOutput
    $env:SPIRE_MOUSE_VERIFY_SUITE = $Suite
    $env:STS2_DEV_SKIP = '1'
    $env:STS2_DEV_WINDOWED = '1'
    $env:SteamAppId = '2868840'
    $taskLogArgument = '"{0}"' -f (Join-Path $taskOutput 'game.log')
    $taskProcess = Start-Process (Join-Path $taskGame 'SlayTheSpire2.exe') -WorkingDirectory $taskGame -ArgumentList '--windowed','--resolution','1280x900','--log-file',$taskLogArgument -WindowStyle Hidden -PassThru
    Write-Output "Verification process $($taskProcess.Id); memory-only save store; installed DLL restored on exit."
    $taskProcess.WaitForExit()
    $taskResultFile = Join-Path $taskOutput 'results.json'
    if (-not (Test-Path -LiteralPath $taskResultFile)) { throw 'No result; inspect game.log.' }
    $taskResult = Get-Content -LiteralPath $taskResultFile -Raw | ConvertFrom-Json
    $taskResult | ConvertTo-Json -Depth 8
    if ($taskResult.status -ne 'passed') { throw "Verification failed: $Suite" }
} finally {
    if ($taskDllBackedUp) { Copy-Item -LiteralPath "$taskOutput\previous.dll" -Destination $taskInstalled }
    foreach ($taskName in $taskPriorEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($taskName, $taskPriorEnvironment[$taskName], 'Process')
    }
    Pop-Location
}

function Get-SourceFingerprint {
    $taskFiles = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src'), (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' -File -Recurse)
    $taskFiles += Get-Item -LiteralPath (Join-Path $PSScriptRoot 'SpireMouse.csproj')
    $taskRows = $taskFiles | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($PSScriptRoot.Length) + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    $taskBytes = [Text.Encoding]::UTF8.GetBytes(($taskRows -join "`n"))
    # Same uppercase hex as Convert.ToHexString(SHA256.HashData(..)), but also runs on Windows PowerShell 5.1.
    $taskSha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($taskSha.ComputeHash($taskBytes)).Replace('-', '') } finally { $taskSha.Dispose() }
}

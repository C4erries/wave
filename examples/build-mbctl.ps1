$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$moduleRoot = Join-Path $repoRoot "wave-mq"
$binDir = Join-Path $scriptDir "bin"
$binaryPath = Join-Path $binDir "mbctl.exe"

New-Item -ItemType Directory -Force -Path $binDir | Out-Null

Push-Location $moduleRoot
try {
    go build -o $binaryPath .\cmd\mbctl
    if ($LASTEXITCODE -ne 0) {
        throw "go build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

Write-Host "Built $binaryPath"

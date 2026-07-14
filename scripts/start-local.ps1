param(
    [switch]$SkipInfra,
    [switch]$WithPostgres,
    [switch]$StartFrontend
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$arguments = @((Join-Path $PSScriptRoot "start-local.mjs"))

if ($SkipInfra) {
    $arguments += "--skip-infra"
}

if ($WithPostgres) {
    $arguments += "--with-postgres"
}

if ($StartFrontend) {
    $arguments += "--frontend"
}

Push-Location $root
try {
    & node @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "The local platform launcher exited with code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

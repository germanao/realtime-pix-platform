param(
    [switch]$SkipInfra,
    [switch]$StartFrontend
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $root "work/runtime-logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Start-HiddenDevProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Command,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory
    )

    $processInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = $env:ComSpec
    $processInfo.Arguments = "/d /c $Command"
    $processInfo.WorkingDirectory = $WorkingDirectory
    $processInfo.UseShellExecute = $true
    $processInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden

    $process = [System.Diagnostics.Process]::Start($processInfo)
    Write-Host "$Name pid=$($process.Id)"
}

if (-not $SkipInfra) {
    docker compose -f (Join-Path $root "infra/docker-compose.yml") up -d
}

$services = @(
    "services/identity-presence-service/IdentityPresenceService.csproj",
    "services/wallet-ledger-service/WalletLedgerService.csproj",
    "services/transaction-service/TransactionService.csproj",
    "services/realtime-events-service/RealtimeEventsService.csproj",
    "services/bot-service/BotService.csproj",
    "services/api-gateway/ApiGateway.csproj"
)

foreach ($service in $services) {
    $project = Join-Path $root $service
    $name = Split-Path (Split-Path $service -Parent) -Leaf
    $stdout = Join-Path $logDir "$name.out.log"
    $stderr = Join-Path $logDir "$name.err.log"
    $command = "dotnet run --project `"$project`" --configuration Release --no-build 1> `"$stdout`" 2> `"$stderr`""
    Start-HiddenDevProcess -Name $name -Command $command -WorkingDirectory $root
}

Write-Host "Backend services are starting. API Gateway: http://localhost:5100"

if ($StartFrontend) {
    $webRoot = Join-Path $root "apps/web"
    $stdout = Join-Path $logDir "web.out.log"
    $stderr = Join-Path $logDir "web.err.log"
    $command = "npm.cmd run dev 1> `"$stdout`" 2> `"$stderr`""
    Start-HiddenDevProcess -Name "web" -Command $command -WorkingDirectory $webRoot
    Write-Host "Frontend is starting. Web: http://localhost:3000"
}

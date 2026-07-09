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
    @{ Project = "services/identity-presence-service/IdentityPresenceService.csproj"; Port = 5101 },
    @{ Project = "services/wallet-ledger-service/WalletLedgerService.csproj"; Port = 5102 },
    @{ Project = "services/transaction-service/TransactionService.csproj"; Port = 5103 },
    @{ Project = "services/realtime-events-service/RealtimeEventsService.csproj"; Port = 5104 },
    @{ Project = "services/bot-service/BotService.csproj"; Port = 5105 },
    @{ Project = "services/api-gateway/ApiGateway.csproj"; Port = 5100 }
)

foreach ($service in $services) {
    $project = Join-Path $root $service.Project
    $name = Split-Path (Split-Path $service.Project -Parent) -Leaf
    $stdout = Join-Path $logDir "$name.out.log"
    $stderr = Join-Path $logDir "$name.err.log"
    $command = "set ASPNETCORE_URLS=http://localhost:$($service.Port)&& dotnet run --project `"$project`" --configuration Release --no-build 1> `"$stdout`" 2> `"$stderr`""
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

param(
    [Parameter(Mandatory = $true)][string]$InstanceId,
    [string]$Region = 'us-east-2',
    [string]$AwsCli = 'aws',
    [switch]$BootOnly
)
$ErrorActionPreference = 'Stop'
# Send only a deployment script; never read or transmit the SSM secret parameter.
$commands = @'
set -eu
cd /opt/realtime-pix
sed -i 's/^docker compose pull$/# Pull images at deployment, not at boot./' /usr/local/sbin/realtime-pix-start
sed -i 's/^docker compose up -d --remove-orphans$/docker compose up -d --pull missing --remove-orphans/' /usr/local/sbin/realtime-pix-start
if ! grep -q '^Restart=on-failure$' /etc/systemd/system/realtime-pix.service; then
  sed -i '/^TimeoutStartSec=900$/a Restart=on-failure\nRestartSec=15' /etc/systemd/system/realtime-pix.service
fi
systemctl daemon-reload
'@
if (-not $BootOnly) {
    $commands += "`ndocker compose pull`ndocker compose up -d --remove-orphans`ndocker compose restart edge`ndocker compose ps`n"
}
$parameters = @{ commands = @($commands); executionTimeout = @('600') } | ConvertTo-Json -Compress
& $AwsCli ssm send-command --region $Region --instance-ids $InstanceId --document-name AWS-RunShellScript --comment 'Deploy cold-start recovery and cached-image boot' --parameters $parameters --query 'Command.CommandId' --output text
if ($LASTEXITCODE -ne 0) { throw 'SSM deployment submission failed.' }

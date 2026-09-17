param([string]$AwsCli = 'C:\Users\germa\AppData\Local\Programs\Amazon\AWSCLIV2-Portable\Amazon\AWSCLIV2\aws.exe')
$ErrorActionPreference = 'Stop'
$credentials = (& $AwsCli configure export-credentials --format process | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0) { throw 'AWS login required.' }
$env:AWS_ACCESS_KEY_ID = $credentials.AccessKeyId
$env:AWS_SECRET_ACCESS_KEY = $credentials.SecretAccessKey
$env:AWS_SESSION_TOKEN = $credentials.SessionToken
$backup = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../../infra/terraform/aws-runtime/backup-databases.sh')).Replace("`r`n", "`n")
$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($backup))
$script = @"
set -eu
python3 - <<'PY'
from pathlib import Path
import base64
script = Path('/usr/local/sbin/realtime-pix-backup')
script.write_bytes(base64.b64decode('$encoded'))
script.chmod(0o700)
Path('/etc/systemd/system/realtime-pix-backup.service').write_text('''[Unit]
Description=Private off-host PIX database backups
After=realtime-pix.service
[Service]
Type=oneshot
ExecStart=/usr/local/sbin/realtime-pix-backup
TimeoutStartSec=600
''')
Path('/etc/systemd/system/realtime-pix-backup.timer').write_text('''[Unit]
Description=Back up PIX databases while the AWS host is running
[Timer]
OnBootSec=3min
OnUnitActiveSec=1h
Unit=realtime-pix-backup.service
[Install]
WantedBy=timers.target
''')
PY
systemctl daemon-reload
systemctl enable --now realtime-pix-backup.timer
systemctl start realtime-pix-backup.service
systemctl show realtime-pix-backup.service -p Result
"@
$parameters = @{commands = @($script); executionTimeout = @('600')} | ConvertTo-Json -Compress
& $AwsCli ssm send-command --region us-east-2 --instance-ids i-0ae6f370495e59423 --document-name AWS-RunShellScript --comment 'Install and verify private AWS database backups' --parameters $parameters --query 'Command.CommandId' --output text
if ($LASTEXITCODE -ne 0) { throw 'Backup installation failed to submit.' }

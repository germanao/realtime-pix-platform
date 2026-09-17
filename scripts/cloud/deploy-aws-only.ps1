param(
    [Parameter(Mandatory = $true)][string]$ImageTag,
    [string]$InstanceId = 'i-0ae6f370495e59423',
    [string]$AwsCli = 'C:\Users\germa\AppData\Local\Programs\Amazon\AWSCLIV2-Portable\Amazon\AWSCLIV2\aws.exe'
)
$ErrorActionPreference = 'Stop'
$migrationCredentials = (& $AwsCli configure export-credentials --format process | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0) { throw 'Run aws login before deployment.' }
$env:AWS_ACCESS_KEY_ID = $migrationCredentials.AccessKeyId
$env:AWS_SECRET_ACCESS_KEY = $migrationCredentials.SecretAccessKey
$env:AWS_SESSION_TOKEN = $migrationCredentials.SessionToken
if ($ImageTag -notmatch '^aws-[0-9a-f]{40}$') { throw 'Provide the exact published commit tag.' }
$source = Join-Path $PSScriptRoot '../../infra/terraform/aws-runtime'
$payload = @{}
foreach ($name in @('compose.aws.yml', 'bus-schema.sql', 'migrate-to-aws.py')) {
    $content = [IO.File]::ReadAllText((Join-Path $source $name)).Replace("`r`n", "`n")
    $payload[$name] = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($content))
}
$json = $payload | ConvertTo-Json -Compress
$script = @"
set -eu
python3 - <<'PY'
from pathlib import Path
import json, base64, os
os.umask(0o077)
stage = Path('/opt/realtime-pix/migration-aws-only')
stage.mkdir(exist_ok=True)
for name, encoded in json.loads('$json').items():
    (stage / name).write_bytes(base64.b64decode(encoded))
PY
python3 /opt/realtime-pix/migration-aws-only/migrate-to-aws.py '$ImageTag'
"@
$parameters = @{ commands = @($script); executionTimeout = @('1800') } | ConvertTo-Json -Compress
& $AwsCli ssm send-command --region us-east-2 --instance-ids $InstanceId --document-name AWS-RunShellScript --comment 'Migrate Azure application data into AWS-only runtime' --parameters $parameters --query 'Command.CommandId' --output text
if ($LASTEXITCODE -ne 0) { throw 'Migration command submission failed.' }

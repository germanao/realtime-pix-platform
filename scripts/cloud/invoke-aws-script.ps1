param(
    [Parameter(Mandatory = $true)][string]$ScriptPath,
    [Parameter(Mandatory = $true)][string]$InstanceId,
    [string]$Region = 'us-east-2',
    [int]$TimeoutSeconds = 1800,
    [string]$AwsCli = 'aws'
)
$ErrorActionPreference = 'Stop'
# Refresh through the login session's own region, then use ephemeral credentials
# for regional API calls. Never persist or print these credentials.
$runtimeCredentials = (& $AwsCli configure export-credentials --format process | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0) { throw 'Run aws login before deploying.' }
$env:AWS_ACCESS_KEY_ID = $runtimeCredentials.AccessKeyId
$env:AWS_SECRET_ACCESS_KEY = $runtimeCredentials.SecretAccessKey
$env:AWS_SESSION_TOKEN = $runtimeCredentials.SessionToken
$script = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $ScriptPath)).Replace("`r`n", "`n")
$parameters = @{ commands = @($script); executionTimeout = @("$TimeoutSeconds") } | ConvertTo-Json -Compress
& $AwsCli ssm send-command --region $Region --instance-ids $InstanceId --document-name AWS-RunShellScript --comment 'Realtime PIX maintenance operation' --parameters $parameters --query 'Command.CommandId' --output text
if ($LASTEXITCODE -ne 0) { throw 'SSM command submission failed.' }

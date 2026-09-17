param(
    [Parameter(Mandatory = $true)][string]$ScriptPath,
    [string]$InstanceId = 'i-0ae6f370495e59423',
    [string]$Region = 'us-east-2',
    [int]$TimeoutSeconds = 1800,
    [string]$AwsCli = 'C:\Users\germa\AppData\Local\Programs\Amazon\AWSCLIV2-Portable\Amazon\AWSCLIV2\aws.exe'
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
& $AwsCli ssm send-command --region $Region --instance-ids $InstanceId --document-name AWS-RunShellScript --comment 'Realtime PIX AWS migration operation' --parameters $parameters --query 'Command.CommandId' --output text
if ($LASTEXITCODE -ne 0) { throw 'SSM command submission failed.' }

param(
    [string]$GitHubUrl = "https://github.com/germanao/realtime-pix-platform.git",
    [string]$BackupBranch = "archive/pre-terraform-migration-20260702",
    [string]$MainBranch = "main"
)

$ErrorActionPreference = "Stop"

$remoteHead = (git ls-remote $GitHubUrl "refs/heads/$MainBranch").Split("`t")[0]
if (-not $remoteHead) {
    throw "Could not read GitHub $MainBranch from $GitHubUrl."
}

Write-Host "GitHub $MainBranch currently points at $remoteHead"

git fetch $GitHubUrl "$MainBranch`:refs/remotes/github/$MainBranch"
git push $GitHubUrl "refs/remotes/github/$MainBranch`:refs/heads/$BackupBranch"

Write-Host "Backup branch pushed: $BackupBranch"
Write-Host "Replacing GitHub $MainBranch with the current local $MainBranch by force-with-lease..."

git push $GitHubUrl "refs/heads/$MainBranch`:refs/heads/$MainBranch" "--force-with-lease=refs/heads/$MainBranch`:$remoteHead"

if ((git remote get-url origin 2>$null) -match "dev.azure.com") {
    git remote rename origin azure-devops
}

if (git remote get-url origin 2>$null) {
    git remote set-url origin $GitHubUrl
} else {
    git remote add origin $GitHubUrl
}

git fetch origin
Write-Host "Migration complete. Verify GitHub Actions before deleting Azure DevOps resources."

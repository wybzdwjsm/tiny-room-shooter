# Push the project to GitHub.
#
# NOTE: This script is intentionally ASCII-only.
# Windows PowerShell 5.1 reads BOM-less .ps1 files as ANSI (GBK),
# so non-ASCII characters become garbage and break parsing.
#
# Usage: open PowerShell in the project root and run:
#   .\push-to-github.ps1
#
# If the GitHub repo does not exist yet, create an EMPTY one first at:
#   https://github.com/new?name=tiny-room-shooter
# (do NOT add README / .gitignore / license, or the first push will be rejected)

$ErrorActionPreference = 'Stop'

$owner  = 'wybzdwjsm'
$repo   = 'tiny-room-shooter'
$remote = "https://github.com/$owner/$repo.git"

Write-Host "Repository: $owner/$repo"

if (-not (Test-Path '.\Assets\Scripts\RunManager.cs')) {
    throw 'Run this script from the project root (Assets\Scripts\RunManager.cs not found).'
}

$current = git remote get-url origin 2>$null
if ($current -ne $remote) {
    git remote remove origin 2>$null
    git remote add origin $remote
    Write-Host "Remote set to $remote"
}

git branch -M main | Out-Null
Write-Host ('Current commit: ' + (git log --oneline -1))

Write-Host 'Pushing...'
Write-Host 'If you get 404 / Repository not found, create the empty repo first at https://github.com/new?name=tiny-room-shooter'
git push -u origin main

if (-not (git tag -l 'v1.0.0')) {
    git tag -a v1.0.0 -m 'The Lamplighter v1.0 - story, endless mode, player readability'
}
git push origin v1.0.0

Write-Host ''
Write-Host 'DONE.'
Write-Host "  https://github.com/$owner/$repo"
Write-Host "  https://github.com/$owner/$repo/releases/tag/v1.0.0"

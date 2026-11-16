# 一键推送到 GitHub
#
# 我这边（沙箱）到 github.com 的 TCP 是被拦掉的（443 不通、HTTPS 走代理返回 502），
# 所以这一步必须在你自己的终端里跑。仓库已经提交好、分支是 main，只差 push。
#
# 用法：在项目根目录右键 →「在终端中打开」，然后：
#   .\push-to-github.ps1
#
# 第一次推送时 Git 凭据管理器会弹浏览器要你授权 GitHub（本机已缓存 wybzdwjsm 的凭据，
# 可能直接就过了）。不需要 Personal Access Token。

$ErrorActionPreference = 'Stop'

$repo   = 'tiny-room-shooter'
$owner  = 'wybzdwjsm'
$remote = "https://github.com/$owner/$repo.git"

Write-Host "→ 仓库：$owner/$repo" -ForegroundColor Cyan

# 1) 确认已经在项目里
if (-not (Test-Path '.\Assets\Scripts\RunManager.cs')) {
    throw '请在项目根目录运行这个脚本（找不到 Assets\Scripts\RunManager.cs）'
}

# 2) 远程地址
$current = git remote get-url origin 2>$null
if ($current -ne $remote) {
    git remote remove origin 2>$null
    git remote add origin $remote
    Write-Host "→ 已设置 origin = $remote"
}

# 3) 分支
git branch -M main | Out-Null
Write-Host ('→ 当前提交：' + (git log --oneline -1))

# 4) 目标仓库还没建的话，git 会报 Repository not found，
#    先去 https://github.com/new?name=tiny-room-shooter 建一个空仓库（不要勾 README）
Write-Host '→ 推送中…（如果报 404 / Repository not found，先去 https://github.com/new 建空仓库）' -ForegroundColor Yellow
git push -u origin main

# 5) 打 tag
if (-not (git tag -l 'v1.0.0')) {
    git tag -a v1.0.0 -m '守灯人 v1.0 —— 剧情、无尽模式、玩家辨识度'
}
git push origin v1.0.0

Write-Host ''
Write-Host '✓ 完成：' -ForegroundColor Green
Write-Host "  https://github.com/$owner/$repo"
Write-Host "  https://github.com/$owner/$repo/releases/tag/v1.0.0"

<#
  把本项目推送到 GitHub

  用法（在项目目录里执行）：
      powershell -ExecutionPolicy Bypass -File push-to-github.ps1 -User 你的GitHub用户名

  可选参数：
      -Repo    仓库名，默认 xiangqi-trainer
      -Email   提交邮箱，默认 <用户名>@users.noreply.github.com
      -Name    提交署名，默认与用户名相同
      -Yes     跳过“是否已创建空仓库”的确认

  说明：推 GitHub 需要身份验证。第一次推送时 Git Credential Manager 会自动弹出浏览器
        让你登录 GitHub（或用 Personal Access Token），登录一次以后就不用再输。
#>
param(
    [Parameter(Mandatory = $true)][string]$User,
    [string]$Repo = "xiangqi-trainer",
    [string]$Email = "",
    [string]$Name = "",
    [switch]$Yes
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

function Say($msg, $color = "Gray") { Write-Host $msg -ForegroundColor $color }

# ---------- 1. 找到 git ----------
function Resolve-Git {
    $cmd = Get-Command git -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $bundled = Join-Path $root ".tools\PortableGit\cmd\git.exe"
    if (Test-Path $bundled) { return $bundled }
    throw "没找到 git。请安装 Git for Windows（https://git-scm.com/download/win），或用国内镜像下载便携版： https://registry.npmmirror.com/-/binary/git-for-windows/"
}
$git = Resolve-Git
Say "git     : $git"

# ---------- 2. 先确认 GitHub 真的连得上 ----------
Say ""
Say "检查 GitHub 连通性 ..."
$blocked = $false
try {
    $ip = (Resolve-DnsName github.com -Type A -ErrorAction Stop | Where-Object { $_.IPAddress } | Select-Object -First 1).IPAddress
    if ($ip -match '^(127\.|0\.|::1)') {
        $blocked = $true
        Say "  github.com 被解析到 $ip" "Yellow"
        Say "  本机 hosts 里还有 GitHub 屏蔽条目（常见于 S302 / 加速器类工具）。" "Yellow"
        Say "  请退出该工具，并删掉 hosts 里以 #S302 结尾的 github 相关行，否则推送必定失败。" "Yellow"
    }
    else { Say "  github.com -> $ip" }
}
catch { Say "  无法解析 github.com：$($_.Exception.Message)" "Yellow" }

if ($blocked -and -not $Yes) {
    $go = Read-Host "仍然继续吗？(y/N)"
    if ($go -ne "y" -and $go -ne "Y") { Say "已取消。"; exit 1 }
}

# ---------- 3. 确保仓库存在（local 配置只能写进 .git，所以必须先 init）----------
$hasGit = Test-Path (Join-Path $root ".git")
if (-not $hasGit) {
    Say ""
    Say "初始化仓库 ..." "Cyan"
    & $git init | Out-Null
}

# ---------- 4. 提交身份 ----------
if (-not $Email) { $Email = "$User@users.noreply.github.com" }
if (-not $Name) { $Name = $User }
Say ""
Say "提交署名: $Name <$Email>"
& $git config user.name $Name
& $git config user.email $Email
if ($LASTEXITCODE -ne 0) { throw "写入 git 身份失败，请检查上面的报错" }

# ---------- 5. 首次提交 / 提交本地改动 ----------
& $git add -A

# 注意：这里用 for-each-ref 而不是 rev-parse @{u}。
# rev-parse 在“没有 upstream”时会把 fatal 写到 stderr，被 $ErrorActionPreference=Stop 当成
# 终止性错误，导致脚本中断；for-each-ref 没有 upstream 时只会输出空行。
$prevEap = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$null = & $git rev-parse --verify --quiet HEAD
$hasCommit = ($LASTEXITCODE -eq 0)
$upstream = (& $git for-each-ref --format="%(upstream)" refs/heads/main | Select-Object -First 1)
$ErrorActionPreference = $prevEap
$hasUpstream = ("$upstream").Trim().Length -gt 0

if (-not $hasCommit) {
    Say "创建首个提交 ..." "Cyan"
    & $git commit -m "中国象棋新手陪练：首个版本"
    if ($LASTEXITCODE -ne 0) { throw "提交失败，请检查上面的 git 报错" }
}
else {
    $dirty = & $git status --porcelain
    if ($dirty) {
        Say "提交本地改动 ..." "Cyan"
        & $git commit -m "更新" | Out-Null
    }
    # 只有“署名还是占位身份”且“还没推送过”时才改作者，避免重写已推送的历史
    $curAuthor = & $git --no-pager log --format="%an" -1
    if (-not $hasUpstream -and $curAuthor -eq "xiangqi-trainer") {
        Say "把提交署名改成你 ..." "Cyan"
        & $git commit --amend --reset-author --no-edit | Out-Null
    }
}
& $git branch -M main

Say ""
Say "当前提交：" "Cyan"
& $git --no-pager log --oneline -1
$tracked = (& $git ls-files | Measure-Object -Line).Lines
Say "已跟踪文件：$tracked 个"

# ---------- 5. 配置远程 ----------
$url = "https://github.com/$User/$Repo.git"
$remotes = & $git remote
if ($remotes -contains "origin") { & $git remote set-url origin $url }
else { & $git remote add origin $url }
Say ""
Say "远程仓库: origin -> $url"

if (-not $blocked -and -not $Yes) {
    Say ""
    Say "如果是第一次推送，请先在浏览器建一个【空仓库】：" "Cyan"
    Say "      https://github.com/new"
    Say "      仓库名填：$Repo     （不要勾选 Add a README / .gitignore / license）"
    Read-Host "建好后按回车开始推送"
}

# ---------- 6. 推送 ----------
Say ""
Say "开始推送 ..." "Cyan"
& $git push -u origin main
if ($LASTEXITCODE -ne 0) {
    Say ""
    Say "推送失败。常见原因：" "Yellow"
    Say "  1) 还没在 GitHub 上创建空仓库 —— 先去 https://github.com/new 创建 $Repo" "Yellow"
    Say "  2) hosts 里 GitHub 仍被屏蔽 —— 退出 S302 类工具并清理 hosts" "Yellow"
    Say "  3) 登录/令牌没通过 —— 重新执行本脚本，按提示在浏览器里登录 GitHub" "Yellow"
    exit 1
}

Say ""
Say "完成！仓库地址： https://github.com/$User/$Repo" "Green"

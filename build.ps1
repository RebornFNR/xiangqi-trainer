# 一键构建：编译 ChinaChess.exe，并跑规则/AI 自测
# 用法： powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

function Find-Csc {
    $cands = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )
    foreach ($c in $cands) { if (Test-Path $c) { return $c } }
    throw "找不到 .NET Framework 的 csc.exe，无法编译。"
}

$csc = Find-Csc
Write-Host "编译器: $csc" -ForegroundColor DarkGray

New-Item -ItemType Directory -Force -Path "$root\build" | Out-Null

$refs = @("/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll", "/r:System.Windows.Forms.dll")
$common = @("/nologo", "/codepage:65001", "/optimize+")

# 1) 主程序
$appArgs = @("/target:winexe", "/out:$root\ChinaChess.exe") + $common + $refs + @(
    "/win32icon:$root\assets\icon.ico",
    "/win32manifest:$root\src\app.manifest",
    "$root\src\Xiangqi.cs", "$root\src\Engine.cs", "$root\src\BoardControl.cs",
    "$root\src\MainForm.cs", "$root\src\Program.cs"
)
Write-Host "编译 ChinaChess.exe ..." -ForegroundColor Cyan
& $csc $appArgs
if ($LASTEXITCODE -ne 0) { throw "主程序编译失败" }

# 2) 自测程序（规则 perft + 记谱 + 引擎）
$testArgs = @("/target:exe", "/out:$root\build\selftest.exe") + $common + $refs + @(
    "$root\src\Xiangqi.cs", "$root\src\Engine.cs", "$root\tools\SelfTest.cs"
)
Write-Host "编译 selftest.exe ..." -ForegroundColor Cyan
& $csc $testArgs
if ($LASTEXITCODE -ne 0) { throw "自测程序编译失败" }

Write-Host "运行自测 ..." -ForegroundColor Cyan
Push-Location "$root\build"
try {
    & "$root\build\selftest.exe"
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}

$exe = Get-Item "$root\ChinaChess.exe"
Write-Host ""
Write-Host ("完成：{0}  ({1:N0} 字节)" -f $exe.Name, $exe.Length) -ForegroundColor Green
if ($code -ne 0) { Write-Host "注意：自测有失败项" -ForegroundColor Red; exit 1 }
Write-Host "双击 ChinaChess.exe 即可开始下棋。" -ForegroundColor Green

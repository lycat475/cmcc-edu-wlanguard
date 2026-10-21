# 编译 C# 版（.NET Framework 4.8，系统自带运行时，输出单个 exe）
# 用法: powershell -ExecutionPolicy Bypass -File .\build.ps1

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$srcDir = Join-Path $root 'src'
$distDir = Join-Path $root 'dist'
$outExe = Join-Path $distDir 'cmcc-edu-guard.exe'

$cscPath = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $cscPath)) { throw "找不到 csc.exe: $cscPath" }

$sourceFiles = Get-ChildItem $srcDir -Filter '*.cs' | Sort-Object Name
if ($sourceFiles.Count -eq 0) { throw 'src 目录里没有 .cs 文件' }

New-Item -ItemType Directory -Path $distDir -Force | Out-Null

# 把 VERSION 里的版本号生成成一个常量编进 exe，界面上显示用。
# 放在临时目录，不弄脏 src。
$version = '0.0.0'
$versionFile = Join-Path $root 'VERSION'
if (Test-Path $versionFile) { $version = ((Get-Content $versionFile -Raw) -replace '\s', '') }
if (-not $version) { $version = '0.0.0' }

$genDir = Join-Path $env:TEMP 'cmcc-gen'
New-Item -ItemType Directory -Path $genDir -Force | Out-Null
$genFile = Join-Path $genDir 'AppVersion.g.cs'
$genCode = @"
// 由 build.ps1 自动生成，不要手改
namespace CmccEdu
{
    internal static class AppInfo
    {
        public const string Version = "VERSION_PLACEHOLDER";
    }
}
"@
$genCode = $genCode.Replace('VERSION_PLACEHOLDER', $version)
[System.IO.File]::WriteAllText($genFile, $genCode, (New-Object System.Text.UTF8Encoding($false)))

$refList = @(
    'System.dll'
    'System.Core.dll'
    'System.Drawing.dll'
    'System.Windows.Forms.dll'
    'System.Web.Extensions.dll'
)

$iconPath = Join-Path $root 'assets\app.ico'

# 注意：不能叫 $args，那是 PowerShell 的自动变量
$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/debug-'
    ('/out:' + $outExe)
    ('/win32icon:' + $iconPath)
)
foreach ($r in $refList) { $cscArgs += ('/r:' + $r) }
foreach ($f in $sourceFiles) { $cscArgs += $f.FullName }
$cscArgs += $genFile   # 版本号常量

Write-Host ('编译 ' + ($sourceFiles.Count + 1) + ' 个源文件（含版本号常量）…')
& $cscPath $cscArgs
if ($LASTEXITCODE -ne 0) { throw ('编译失败，退出码 ' + $LASTEXITCODE) }

$item = Get-Item $outExe
$hash = (Get-FileHash $outExe -Algorithm SHA256).Hash
Write-Host ''
Write-Host ('已生成: ' + $outExe)
Write-Host ('  大小: ' + [math]::Round($item.Length / 1KB, 1) + ' KB')
Write-Host ('  SHA256: ' + $hash)

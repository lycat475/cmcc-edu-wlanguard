<#
.SYNOPSIS
    一条命令完成发版：编译 → 打包 → 提交 → 打标签 → 建 Release → 上传 → 关闭旧版本下载。

.DESCRIPTION
    流程（按顺序）：
      1. 校验版本号未被发布过
      2. 写入 VERSION 并编译
      3. 打包完整版（release\）和无文档版（下载目录）
      4. 同步到 git 工作副本，编译验证，提交并推送
      5. 打标签（轻量推送前先建注释标签）
      6. 建 GitHub Release 并上传无文档版 zip
      7. 关闭所有旧版本的下载渠道：
           - 删除旧 Release 的附件（安装包）
           - 删除旧标签（源码 zip 由标签生成，删标签才能断掉）
         旧版本的更新说明保留，只是没有下载入口。
      8. 把归档哈希写回 CHANGELOG 并推送
      9. 可选：部署到运行目录

    令牌从 Git 凭据管理器读取，不落盘、不打印。

.PARAMETER Version
    要发布的版本号，例如 2.1.6。

.PARAMETER RepoDir
    git 工作副本目录（默认 %USERPROFILE%\Documents\cmcc-repo）。

.PARAMETER Repo
    GitHub 仓库，默认 lycat475/cmcc-edu-wlanguard。

.PARAMETER DeployTo
    运行目录，填写后会把新 exe 复制过去并重启。

.PARAMETER DryRun
    只打印将要执行的动作，不写文件、不联网。

.EXAMPLE
    .\release.ps1 -Version 2.1.6

.EXAMPLE
    .\release.ps1 -Version 2.1.6 -DeployTo 'D:\Apps\cmcc'
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$RepoDir = (Join-Path $env:USERPROFILE 'Documents\cmcc-repo'),
    [string]$Repo = 'lycat475/cmcc-edu-wlanguard',
    [string]$DeployTo = '',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exeName = 'cmcc-edu-guard.exe'

function Step($n, $text) { Write-Host ''; Write-Host ('=== ' + $n + '. ' + $text + ' ===') }
function Act($text) { if ($DryRun) { Write-Host ('  [dry] ' + $text) } else { Write-Host ('  ' + $text) } }

function Get-GitHubToken {
    # 用 git 自己的凭据接口，不依赖 GCM 的安装路径
    $cred = ("protocol=https`nhost=github.com`n`n" | & git credential fill 2>&1)
    $line = $cred | Where-Object { $_ -like 'password=*' } | Select-Object -First 1
    if (-not $line) { throw '读不到 GitHub 令牌，请先 git push 一次让凭据管理器记住' }
    return ($line -replace '^password=', '')
}

function Invoke-Api($method, $uri, $bodyText = $null, $contentType = 'application/json; charset=utf-8') {
    $h = @{ Authorization = 'token ' + $script:token; Accept = 'application/vnd.github+json'; 'User-Agent' = 'cmcc-release' }
    if ($bodyText) {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($bodyText)
        return Invoke-RestMethod -Uri $uri -Method $method -Headers $h -Body $bytes -ContentType $contentType
    }
    return Invoke-RestMethod -Uri $uri -Method $method -Headers $h
}

# ---------------------------------------------------------------- 0. 前置检查
Step 0 '前置检查'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw ('版本号格式不对：' + $Version) }
$tag = 'v' + $Version
$script:token = Get-GitHubToken

$exePath = Join-Path $root ('dist\' + $exeName)
$fullZip = Join-Path $root ('release\CMCC-EDU网络守护_' + $tag + '.zip')
$downloads = (New-Object -ComObject Shell.Application).NameSpace('shell:Downloads').Self.Path
$liteZip = Join-Path $downloads ('cmcc-edu-wlanguard-' + $tag + '.zip')

$existing = Invoke-Api 'GET' ("https://api.github.com/repos/$Repo/releases")
if ($existing | Where-Object { $_.tag_name -eq $tag }) { throw ('版本 ' + $tag + ' 已经发布过了，请换一个版本号') }
Act ('目标版本 ' + $tag + ' 未发布过')

# ---------------------------------------------------------------- 1. VERSION + 编译
Step 1 '写入 VERSION 并编译'
if (-not $DryRun) {
    [System.IO.File]::WriteAllText((Join-Path $root 'VERSION'), ($Version + "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
}
Act ('VERSION -> ' + $Version)
if (-not $DryRun) { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') | Out-Null }
Act '编译完成'

# ---------------------------------------------------------------- 2. 打包
Step 2 '打包完整版'
if (-not $DryRun) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'package.ps1') -Version $Version | Out-Null
}
Act ('归档 -> ' + $fullZip)

Step 3 '打包无文档版'
$stage = Join-Path $env:TEMP ('cmcc_lite_' + [guid]::NewGuid().ToString('N').Substring(0, 6))
$pkgDir = Join-Path $stage 'cmcc-edu-guard'
if (-not $DryRun) {
    New-Item -ItemType Directory -Path $pkgDir -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $pkgDir 'assets') -Force | Out-Null
    Copy-Item $exePath $pkgDir -Force
    Copy-Item (Join-Path $root 'assets\app.ico') (Join-Path $pkgDir 'assets') -Force
    [System.IO.File]::WriteAllText((Join-Path $pkgDir 'VERSION'), ($Version + "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
    $tpl = [ordered]@{
        portal = ''; acName = ''; ssid = 'CMCC-EDU'; triggerUrl = 'http://www.baidu.com/'
        loginMode = 'dynamic'; interval = 60; autoload = 'checked'; guiStartMode = 'tray'
        usernameEnc = ''; passwordEnc = ''; autoCookiesEnc = ''
    }
    [System.IO.File]::WriteAllText((Join-Path $pkgDir 'config.json'), ($tpl | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
    if (Test-Path $liteZip) { Remove-Item $liteZip -Force }
    Compress-Archive -Path $pkgDir -DestinationPath $liteZip -CompressionLevel Optimal
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}
Act ('整合包 -> ' + $liteZip)

# ---------------------------------------------------------------- 3. 同步并提交
Step 4 '同步工作副本并提交'
Push-Location $RepoDir
try {
    if (-not $DryRun) {
        & git pull --ff-only 2>&1 | Out-Null
        foreach ($f in 'CHANGELOG.md', 'VERSION', 'README.md', 'build.ps1', 'package.ps1', 'release.ps1') {
            $src = Join-Path $root $f
            if (Test-Path $src) { Copy-Item $src (Join-Path $RepoDir $f) -Force }
        }
        Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { Copy-Item $_.FullName (Join-Path $RepoDir 'src') -Force }
        if (Test-Path (Join-Path $root 'docs\panel.png')) { Copy-Item (Join-Path $root 'docs\panel.png') (Join-Path $RepoDir 'docs') -Force }
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoDir 'build.ps1') | Out-Null

        & git config user.name 'lycat475'
        & git config user.email 'lycat475@users.noreply.github.com'
        & git add -A
        & git commit -m ($Version + ': 发布 ' + $tag) 2>&1 | Out-Null
        & git tag -a $tag -m ($Version + '：发布') 2>&1 | Out-Null
        & git push origin main 2>&1 | Out-Null
        & git push origin $tag 2>&1 | Out-Null
    }
    Act ('提交并推送 ' + $tag)
} finally { Pop-Location }

# ---------------------------------------------------------------- 4. 建 Release 并上传
Step 5 '创建 GitHub Release 并上传'
$body = '可运行整合包。解压后双击 `' + $exeName + '` 即可，无需安装运行时。'
$payload = @{ tag_name = $tag; name = ('CMCC-EDU 网络守护 ' + $tag); body = $body; draft = $false; prerelease = $false } | ConvertTo-Json -Depth 3
$release = $null
if (-not $DryRun) {
    $release = Invoke-Api 'POST' ("https://api.github.com/repos/$Repo/releases") $payload
    $uploadUrl = ($release.upload_url -replace '\{.*\}', '') + '?name=' + [uri]::EscapeDataString([System.IO.Path]::GetFileName($liteZip))
    $h = @{ Authorization = 'token ' + $script:token; Accept = 'application/vnd.github+json'; 'User-Agent' = 'cmcc-release' }
    Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $h -ContentType 'application/zip' -InFile $liteZip | Out-Null
    Act ('上传 ' + (Split-Path $liteZip -Leaf))
} else { Act ('[dry] 上传 ' + (Split-Path $liteZip -Leaf)) }

# ---------------------------------------------------------------- 5. 关闭旧版本下载
Step 6 '关闭旧版本下载渠道'
$all = Invoke-Api 'GET' ("https://api.github.com/repos/$Repo/releases")
$old = $all | Where-Object { $_.tag_name -ne $tag }
foreach ($r in $old) {
    foreach ($a in $r.assets) {
        if (-not $DryRun) { Invoke-Api 'DELETE' ("https://api.github.com/repos/$Repo/releases/assets/" + $a.id) | Out-Null }
        Act ('删除附件 ' + $r.tag_name + ' -> ' + $a.name)
    }
}
$tags = Invoke-Api 'GET' ("https://api.github.com/repos/$Repo/git/refs/tags")
foreach ($t in $tags) {
    $name = $t.ref.Replace('refs/tags/', '')
    if ($name -eq $tag) { continue }
    if (-not $DryRun) {
        Push-Location $RepoDir
        try { & git push origin --delete $name 2>&1 | Out-Null; & git tag -d $name 2>&1 | Out-Null } finally { Pop-Location }
    }
    Act ('删除标签 ' + $name)
}
Act '旧版本更新说明保留，下载入口已关闭'

# ---------------------------------------------------------------- 6. 回填哈希
Step 7 '回填归档哈希'
if (-not $DryRun) {
    $hash = (Get-FileHash $fullZip -Algorithm SHA256).Hash
    $size = [math]::Round((Get-Item $fullZip).Length / 1KB, 1)
    $changelog = Join-Path $root 'CHANGELOG.md'
    $lines = Get-Content $changelog -Encoding UTF8
    $out = @()
    $inserted = $false
    $inSection = $false
    foreach ($ln in $lines) {
        if (-not $inSection -and $ln -like ('## ' + $tag + '*')) { $inSection = $true; $out += $ln; continue }
        if ($inSection -and $ln -like '## *' -and -not $inserted) {
            $out += '**校验**'
            $out += ''
            $out += '```'
            $out += '文件: CMCC-EDU网络守护_' + $tag + '.zip'
            $out += '大小: ' + $size + ' KB'
            $out += 'SHA256: ' + $hash
            $out += '```'
            $out += ''
            $inserted = $true
            $inSection = $false
        }
        $out += $ln
    }
    if ($inserted) {
        [System.IO.File]::WriteAllLines($changelog, $out, (New-Object System.Text.UTF8Encoding($false)))
        Copy-Item $changelog (Join-Path $RepoDir 'CHANGELOG.md') -Force
        Push-Location $RepoDir
        try {
            & git add -A
            & git commit -m ('CHANGELOG: 记录 ' + $tag + ' 归档哈希') 2>&1 | Out-Null
            & git push origin main 2>&1 | Out-Null
        } finally { Pop-Location }
        Act ('哈希 ' + $hash.Substring(0, 16) + '… 已写入并推送')
    } else { Act '未找到 CHANGELOG 插入点，跳过' }
} else { Act '[dry] 回填哈希' }

# ---------------------------------------------------------------- 7. 部署
if ($DeployTo) {
    Step 8 '部署到运行目录'
    if (-not $DryRun) {
        Get-Process | Where-Object { $_.ProcessName -like 'cmcc-edu-guard*' } | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 3
        Copy-Item $exePath (Join-Path $DeployTo $exeName) -Force
        Start-Process -FilePath (Join-Path $DeployTo $exeName)
    }
    Act ('部署到 ' + $DeployTo)
}

Write-Host ''
Write-Host ('发布完成: ' + $tag)
Write-Host ('  Release  https://github.com/' + $Repo + '/releases/tag/' + $tag)
Write-Host ('  整合包   ' + $liteZip)
Write-Host ('  归档     ' + $fullZip)

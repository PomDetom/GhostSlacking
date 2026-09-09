[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateSet('Patch', 'Minor', 'Major')]
    [string]$Increment,

    [switch]$SkipTests,
    [switch]$OpenOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'GhostSlacking.sln'
$installerScript = Join-Path $repositoryRoot 'installer\build-installer.ps1'
$installerOutputRoot = Join-Path $repositoryRoot 'artifacts\installer'
$releaseOutputRoot = Join-Path $repositoryRoot 'artifacts\releases'

function Get-NextVersion {
    param(
        [Parameter(Mandatory)][version]$CurrentVersion,
        [Parameter(Mandatory)][string]$Part
    )

    switch ($Part) {
        'Major' { return [version]::new($CurrentVersion.Major + 1, 0, 0) }
        'Minor' { return [version]::new($CurrentVersion.Major, $CurrentVersion.Minor + 1, 0) }
        default { return [version]::new($CurrentVersion.Major, $CurrentVersion.Minor, $CurrentVersion.Build + 1) }
    }
}

function Get-AutomaticVersion {
    param([Parameter(Mandatory)][string]$IncrementPart)

    $knownVersions = [System.Collections.Generic.List[version]]::new()

    if (Test-Path -LiteralPath $installerOutputRoot) {
        Get-ChildItem -LiteralPath $installerOutputRoot -File -Filter 'GhostSlacking-*-win-x64.msi' |
            ForEach-Object {
                if ($_.BaseName -match '^GhostSlacking-(\d+\.\d+\.\d+)-win-x64$') {
                    $knownVersions.Add([version]$Matches[1])
                }
            }
    }

    $gitCommand = Get-Command git -ErrorAction SilentlyContinue
    if ($null -ne $gitCommand) {
        $tags = & $gitCommand.Source -C $repositoryRoot tag --list 'v[0-9]*' 2>$null
        if ($LASTEXITCODE -eq 0) {
            foreach ($tag in $tags) {
                if ($tag -match '^v(\d+\.\d+\.\d+)$') {
                    $knownVersions.Add([version]$Matches[1])
                }
            }
        }
    }

    if ($knownVersions.Count -gt 0) {
        $latestVersion = $knownVersions | Sort-Object -Descending | Select-Object -First 1
        return (Get-NextVersion -CurrentVersion $latestVersion -Part $IncrementPart).ToString(3)
    }

    $installerProject = Join-Path $repositoryRoot 'installer\GhostSlacking.Installer.wixproj'
    $projectText = Get-Content -LiteralPath $installerProject -Raw
    if ($projectText -notmatch '<ProductVersion[^>]*>(\d+\.\d+\.\d+)</ProductVersion>') {
        throw '无法从安装器项目确定初始版本，请使用 -Version 指定三段式版本号。'
    }

    return ([version]$Matches[1]).ToString(3)
}

function Select-PackagingStrategy {
    $patchVersion = Get-AutomaticVersion -IncrementPart 'Patch'
    $minorVersion = Get-AutomaticVersion -IncrementPart 'Minor'
    $majorVersion = Get-AutomaticVersion -IncrementPart 'Major'

    while ($true) {
        Write-Host ''
        Write-Host 'GhostSlacking 打包策略' -ForegroundColor Green
        Write-Host "  [1] Patch 完整打包（推荐）  -> $patchVersion"
        Write-Host "  [2] Minor 完整打包          -> $minorVersion"
        Write-Host "  [3] Major 完整打包          -> $majorVersion"
        Write-Host '  [4] 指定版本完整打包'
        Write-Host "  [5] Patch 快速调试包        -> $patchVersion（跳过测试）"
        Write-Host '  [0] 取消'

        $choice = (Read-Host '请选择打包策略 [1]').Trim()
        if ([string]::IsNullOrWhiteSpace($choice)) {
            $choice = '1'
        }

        switch ($choice) {
            '1' {
                return [pscustomobject]@{ Version = $null; Increment = 'Patch'; SkipTests = $false }
            }
            '2' {
                return [pscustomobject]@{ Version = $null; Increment = 'Minor'; SkipTests = $false }
            }
            '3' {
                return [pscustomobject]@{ Version = $null; Increment = 'Major'; SkipTests = $false }
            }
            '4' {
                while ($true) {
                    $customVersion = (Read-Host '请输入三段式版本号，例如 1.2.3').Trim()
                    if ($customVersion -match '^\d+\.\d+\.\d+$') {
                        try {
                            $normalizedVersion = ([version]$customVersion).ToString(3)
                            return [pscustomobject]@{ Version = $normalizedVersion; Increment = $null; SkipTests = $false }
                        }
                        catch {
                        }
                    }

                    Write-Host '版本号无效，请使用 MAJOR.MINOR.PATCH 格式。' -ForegroundColor Yellow
                }
            }
            '5' {
                return [pscustomobject]@{ Version = $null; Increment = 'Patch'; SkipTests = $true }
            }
            '0' {
                return $null
            }
            default {
                Write-Host '无效选项，请输入 0 到 5。' -ForegroundColor Yellow
            }
        }
    }
}

function Invoke-BuildStep {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    Write-Host "`n==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name 失败，退出代码为 $LASTEXITCODE。"
    }
}

function Get-GitMetadata {
    $result = [ordered]@{
        Commit = $null
        IsDirty = $null
    }

    $gitCommand = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $gitCommand) {
        return $result
    }

    $commit = & $gitCommand.Source -C $repositoryRoot rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -eq 0) {
        $result.Commit = "$commit".Trim()
    }

    $status = & $gitCommand.Source -C $repositoryRoot status --porcelain 2>$null
    if ($LASTEXITCODE -eq 0) {
        $result.IsDirty = @($status).Count -gt 0
    }

    return $result
}

if (-not (Test-Path -LiteralPath $solutionPath)) {
    throw "找不到解决方案：$solutionPath"
}

if (-not (Test-Path -LiteralPath $installerScript)) {
    throw "找不到安装器脚本：$installerScript"
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw '未找到 dotnet。请先安装 .NET 8 SDK。'
}

$powerShellExecutable = (Get-Process -Id $PID).Path
if ([string]::IsNullOrWhiteSpace($powerShellExecutable)) {
    throw '无法确定当前 PowerShell 可执行文件。'
}

$sdkVersionText = (& $dotnetCommand.Source --version).Trim()
if ($LASTEXITCODE -ne 0 -or $sdkVersionText -notmatch '^(\d+)\.') {
    throw '无法读取 .NET SDK 版本。'
}

if ([int]$Matches[1] -lt 8) {
    throw "需要 .NET 8 SDK 或更高版本，当前版本为 $sdkVersionText。"
}

$selectedSkipTests = [bool]$SkipTests
$selectedOpenOutput = [bool]$OpenOutput
$useInteractiveStrategy = -not $PSBoundParameters.ContainsKey('Version') -and
    -not $PSBoundParameters.ContainsKey('Increment')

if ($useInteractiveStrategy) {
    $strategy = Select-PackagingStrategy
    if ($null -eq $strategy) {
        Write-Host '已取消打包。' -ForegroundColor Yellow
        exit 0
    }

    if (-not [string]::IsNullOrWhiteSpace($strategy.Version)) {
        $Version = $strategy.Version
    }
    if (-not [string]::IsNullOrWhiteSpace($strategy.Increment)) {
        $Increment = $strategy.Increment
    }
    $selectedSkipTests = $selectedSkipTests -or $strategy.SkipTests

    if (-not $PSBoundParameters.ContainsKey('OpenOutput')) {
        $openChoice = (Read-Host '打包完成后打开输出目录？[Y/n]').Trim()
        $selectedOpenOutput = $openChoice -notmatch '^(?i:n|no)$'
    }
}

if ([string]::IsNullOrWhiteSpace($Increment)) {
    $Increment = 'Patch'
}

$packageVersion = if (-not [string]::IsNullOrWhiteSpace($Version)) {
    ([version]$Version).ToString(3)
}
else {
    Get-AutomaticVersion -IncrementPart $Increment
}

$releaseDirectory = Join-Path $releaseOutputRoot $packageVersion
$sourceMsi = Join-Path $installerOutputRoot "GhostSlacking-$packageVersion-win-x64.msi"
$releaseMsi = Join-Path $releaseDirectory "GhostSlacking-$packageVersion-win-x64.msi"
$checksumPath = "$releaseMsi.sha256"
$manifestPath = Join-Path $releaseDirectory 'release.json'
$logPath = Join-Path $releaseDirectory 'build.log'

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

$transcriptStarted = $false
try {
    Start-Transcript -LiteralPath $logPath -Force | Out-Null
    $transcriptStarted = $true

    Write-Host 'GhostSlacking 自动打包' -ForegroundColor Green
    Write-Host "版本：$packageVersion"
    Write-Host "SDK： $sdkVersionText"
    Write-Host "输出：$releaseDirectory"

    Push-Location $repositoryRoot
    try {
        Invoke-BuildStep -Name '还原 NuGet 依赖' -Action {
            & $dotnetCommand.Source restore $solutionPath
        }

        if (-not $selectedSkipTests) {
            Invoke-BuildStep -Name '运行 Release 自动化测试' -Action {
                & $dotnetCommand.Source test $solutionPath --configuration Release --no-restore
            }
        }
        else {
            Write-Host "`n==> 已跳过自动化测试" -ForegroundColor Yellow
        }

        Invoke-BuildStep -Name '发布应用并生成 MSI' -Action {
            # Run the existing script in a child process so this tool's strict
            # mode cannot alter its established execution semantics.
            & $powerShellExecutable -NoLogo -NoProfile -ExecutionPolicy Bypass `
                -File $installerScript -Version $packageVersion
        }
    }
    finally {
        Pop-Location
    }

    if (-not (Test-Path -LiteralPath $sourceMsi)) {
        throw "打包脚本未生成预期文件：$sourceMsi"
    }

    Copy-Item -LiteralPath $sourceMsi -Destination $releaseMsi -Force
    $msiFile = Get-Item -LiteralPath $releaseMsi
    $hash = Get-FileHash -LiteralPath $releaseMsi -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($msiFile.Name)" |
        Set-Content -LiteralPath $checksumPath -Encoding ascii

    $git = Get-GitMetadata
    $manifest = [ordered]@{
        product = 'GhostSlacking'
        version = $packageVersion
        architecture = 'win-x64'
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        dotnetSdk = $sdkVersionText
        gitCommit = $git.Commit
        gitDirty = $git.IsDirty
        testsSkipped = $selectedSkipTests
        installer = [ordered]@{
            file = $msiFile.Name
            bytes = $msiFile.Length
            sha256 = $hash.Hash.ToLowerInvariant()
        }
    }
    $manifest | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $manifestPath -Encoding utf8

    Write-Host "`n打包成功：$releaseMsi" -ForegroundColor Green
    Write-Host "SHA256：$($hash.Hash.ToLowerInvariant())"
    Write-Host "清单：  $manifestPath"
    Write-Host "日志：  $logPath"
}
finally {
    if ($transcriptStarted) {
        Stop-Transcript | Out-Null
    }
}

if ($selectedOpenOutput) {
    Start-Process explorer.exe -ArgumentList $releaseDirectory
}

Get-Item -LiteralPath $releaseMsi

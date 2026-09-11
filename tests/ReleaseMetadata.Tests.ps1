$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $repositoryRoot 'scripts\release-metadata.ps1'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "GhostSlacking-release-tests-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testRoot | Out-Null

function Invoke-Metadata([string[]]$Arguments, [bool]$ShouldSucceed = $true) {
    & pwsh -NoProfile -File $scriptPath @Arguments *> $null
    $succeeded = $LASTEXITCODE -eq 0
    if ($succeeded -ne $ShouldSucceed) {
        throw "release-metadata.ps1 success=$succeeded, expected=$ShouldSucceed. Arguments: $($Arguments -join ' ')"
    }
}

try {
    foreach ($subject in @(
        'feat: add update details',
        'fix(ui): handle missing notes',
        'feat!: replace update contract',
        'chore(release): prepare 0.2.0',
        'revert: undo broken release')) {
        Invoke-Metadata @('-Mode', 'ValidateCommit', '-Subject', $subject)
    }
    foreach ($subject in @('Add update details', 'feature: unknown type', 'fix:', 'Merge branch dev')) {
        Invoke-Metadata @('-Mode', 'ValidateCommit', '-Subject', $subject) $false
    }
    Invoke-Metadata @('-Mode', 'ValidateVersion', '-Tag', 'v0.2.0')
    Invoke-Metadata @('-Mode', 'ValidateVersion', '-Tag', 'v0.2.0', '-PreviousTag', 'v0.1.2')
    Invoke-Metadata @('-Mode', 'ValidateVersion', '-Tag', 'v0.1.2', '-PreviousTag', 'v0.1.2') $false
    Invoke-Metadata @('-Mode', 'ValidateVersion', '-Tag', 'v0.1.1', '-PreviousTag', 'v0.1.2') $false
    Invoke-Metadata @('-Mode', 'ValidateVersion', '-Tag', '0.2.0') $false

    $validBody = @'
## Summary

<!-- release-notes:zh:start -->
### 新功能
- 增加更新说明窗口。

修复旧版本说明缺失时的显示。
<!-- release-notes:zh:end -->

<!-- release-notes:en:start -->
### Features
- Add an update details window.

Handle releases without structured notes.
<!-- release-notes:en:end -->
'@
    $bodyPath = Join-Path $testRoot 'body.md'
    [System.IO.File]::WriteAllText($bodyPath, $validBody)
    Invoke-Metadata @('-Mode', 'ValidateNotes', '-BodyPath', $bodyPath)

    foreach ($invalidBody in @(
        $validBody.Replace('<!-- release-notes:en:end -->', ''),
        $validBody.Replace('- 增加更新说明窗口。', '- TODO'),
        $validBody.Replace('### Features', '## Features'),
        $validBody.Replace('### 新功能', "### 新功能`n[外部链接](https://example.com)"))) {
        [System.IO.File]::WriteAllText($bodyPath, $invalidBody)
        Invoke-Metadata @('-Mode', 'ValidateNotes', '-BodyPath', $bodyPath) $false
    }
    [System.IO.File]::WriteAllText($bodyPath, $validBody)

    $pullRequestsPath = Join-Path $testRoot 'pull-requests.json'
    $selectedBodyPath = Join-Path $testRoot 'selected.md'
    $releasePr = @{
        merged_at = '2026-09-10T00:00:00Z'
        merge_commit_sha = 'abc123'
        body = $validBody
        base = @{ ref = 'master' }
        head = @{ ref = 'dev' }
    }
    [System.IO.File]::WriteAllText($pullRequestsPath, (ConvertTo-Json @($releasePr) -Depth 5))
    Invoke-Metadata @(
        '-Mode', 'SelectReleasePr',
        '-PullRequestsPath', $pullRequestsPath,
        '-CommitSha', 'abc123',
        '-OutputPath', $selectedBodyPath)
    if ((Get-Content -LiteralPath $selectedBodyPath -Raw) -notmatch '增加更新说明窗口') {
        throw 'Selected release PR body was not written.'
    }

    $wrongBranch = $releasePr.Clone()
    $wrongBranch.head = @{ ref = 'feature' }
    [System.IO.File]::WriteAllText($pullRequestsPath, (ConvertTo-Json @($wrongBranch) -Depth 5))
    Invoke-Metadata @(
        '-Mode', 'SelectReleasePr',
        '-PullRequestsPath', $pullRequestsPath,
        '-CommitSha', 'abc123',
        '-OutputPath', $selectedBodyPath) $false
    [System.IO.File]::WriteAllText($pullRequestsPath, (ConvertTo-Json @($releasePr, $releasePr) -Depth 5))
    Invoke-Metadata @(
        '-Mode', 'SelectReleasePr',
        '-PullRequestsPath', $pullRequestsPath,
        '-CommitSha', 'abc123',
        '-OutputPath', $selectedBodyPath) $false

    $outputPath = Join-Path $testRoot 'release-notes.md'
    Invoke-Metadata @(
        '-Mode', 'Compose',
        '-BodyPath', $bodyPath,
        '-OutputPath', $outputPath,
        '-Tag', 'v0.2.0',
        '-PreviousTag', 'v0.1.2',
        '-RepositoryUrl', 'https://github.com/PomDetom/GhostSlacking')
    $output = Get-Content -LiteralPath $outputPath -Raw
    if ($output -notmatch 'release-notes:zh:start' -or
        $output -notmatch 'release-notes:en:start' -or
        $output -notmatch '/compare/v0\.1\.2\.\.\.v0\.2\.0') {
        throw 'Composed release notes are incomplete.'
    }

    Invoke-Metadata @(
        '-Mode', 'Compose',
        '-BodyPath', $bodyPath,
        '-OutputPath', $outputPath,
        '-Tag', 'v0.2.0',
        '-RepositoryUrl', 'https://github.com/PomDetom/GhostSlacking')
    if ((Get-Content -LiteralPath $outputPath -Raw) -notmatch '/commits/v0\.2\.0') {
        throw 'First-release changelog fallback is missing.'
    }

    Write-Output 'Release metadata tests passed.'
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

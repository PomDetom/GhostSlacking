param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('ValidateCommit', 'ValidateVersion', 'ValidateNotes', 'ValidateFeature', 'ValidateReleasePr', 'SelectReleasePr', 'Compose', 'ComposePullRequests')]
    [string]$Mode,
    [string]$Subject,
    [string]$BodyPath,
    [string]$PullRequestsPath,
    [string]$CommitSha,
    [string]$OutputPath,
    [string]$Tag,
    [string]$PreviousTag,
    [string]$RepositoryUrl,
    [string]$Category
)

$ErrorActionPreference = 'Stop'
$notesMarkers = @{
    ChineseStart = '<!-- release-notes:zh:start -->'
    ChineseEnd = '<!-- release-notes:zh:end -->'
    EnglishStart = '<!-- release-notes:en:start -->'
    EnglishEnd = '<!-- release-notes:en:end -->'
}

$tagPattern = '^v(\d+\.\d+\.\d+)(?:-(alpha|beta|rc)\.(\d+))?$'

function Require-Value([string]$Value, [string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Name is required for mode $Mode."
    }
}

function Read-RequiredFile([string]$Path, [string]$Name) {
    Require-Value $Path $Name
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Name was not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw
}

function Get-NotesSection([string]$Body, [string]$StartMarker, [string]$EndMarker, [string]$Language) {
    $startMatches = [regex]::Matches($Body, [regex]::Escape($StartMarker))
    $endMatches = [regex]::Matches($Body, [regex]::Escape($EndMarker))
    if ($startMatches.Count -ne 1 -or $endMatches.Count -ne 1) {
        throw "Release PR must contain exactly one $Language release-notes marker pair."
    }

    $start = $startMatches[0].Index + $startMatches[0].Length
    $end = $endMatches[0].Index
    if ($end -le $start) {
        throw "$Language release-notes markers are out of order or empty."
    }

    $section = $Body.Substring($start, $end - $start).Trim()
    if ([string]::IsNullOrWhiteSpace($section) -or $section -match '(?im)\bTODO\b|待填写|replace me') {
        throw "$Language release notes are empty or still contain a template placeholder."
    }

    $hasContent = $false
    foreach ($rawLine in ($section -split "`r?`n")) {
        $line = $rawLine.Trim()
        if ($line.Length -eq 0) {
            continue
        }

        if ($line.StartsWith('### ')) {
            if ($line.Length -le 4) {
                throw "$Language release notes contain an empty heading."
            }
            continue
        }

        if ($line.StartsWith('- ')) {
            if ($line.Length -le 2) {
                throw "$Language release notes contain an empty bullet."
            }
            $hasContent = $true
            continue
        }

        if ($line -match '^(#|\* |\+ |> |```|\d+\. |<)|\[[^\]]+\]\([^)]+\)') {
            throw "$Language release notes use unsupported Markdown: $line"
        }

        $hasContent = $true
    }

    if (-not $hasContent) {
        throw "$Language release notes must include at least one bullet or paragraph."
    }

    return $section
}

function Get-ValidatedNotes([string]$Body) {
    $chinese = Get-NotesSection `
        $Body `
        $notesMarkers.ChineseStart `
        $notesMarkers.ChineseEnd `
        'Chinese'
    $english = Get-NotesSection `
        $Body `
        $notesMarkers.EnglishStart `
        $notesMarkers.EnglishEnd `
        'English'
    return @{
        Chinese = $chinese
        English = $english
    }
}

function Get-ReleaseVersion([string]$VersionText, [string]$Name) {
    if ($VersionText -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-(alpha|beta|rc)\.(\d+))?$') {
        throw "$Name is not a supported SemVer release: $VersionText"
    }

    return [pscustomobject]@{
        Major = [int]$Matches[1]
        Minor = [int]$Matches[2]
        Patch = [int]$Matches[3]
        Channel = $Matches[4]
        Number = if ($Matches[5]) { [int]$Matches[5] } else { 0 }
        IsPreRelease = [bool]$Matches[4]
        Text = $VersionText
    }
}

function Compare-ReleaseVersion($Left, $Right) {
    foreach ($part in @('Major', 'Minor', 'Patch')) {
        $result = $Left.$part.CompareTo($Right.$part)
        if ($result -ne 0) { return $result }
    }

    if (-not $Left.IsPreRelease -and -not $Right.IsPreRelease) { return 0 }
    if (-not $Left.IsPreRelease) { return 1 }
    if (-not $Right.IsPreRelease) { return -1 }

    $channelOrder = @{ alpha = 0; beta = 1; rc = 2 }
    $result = $channelOrder[$Left.Channel].CompareTo($channelOrder[$Right.Channel])
    if ($result -ne 0) { return $result }
    return $Left.Number.CompareTo($Right.Number)
}

function Get-TagReleaseVersion([string]$TagValue, [string]$Name = 'Tag') {
    if ($TagValue -notmatch $tagPattern) {
        throw "$Name is invalid: $TagValue"
    }

    $versionText = $Matches[1]
    if ($Matches[2]) { $versionText += "-$($Matches[2]).$($Matches[3])" }
    return Get-ReleaseVersion $versionText $Name
}

function Get-ChangelogLines([string]$Section) {
    $lines = @($Section -split "`r?`n" | ForEach-Object {
        $line = $_.Trim()
        if ($line.StartsWith('- ')) { $line.Substring(2).Trim() }
        elseif ($line -and -not $line.StartsWith('### ')) { $line }
    } | Where-Object { $_ })
    if ($lines.Count -eq 0) { throw 'Release notes must contain at least one bullet or paragraph.' }
    return $lines
}

function Get-PullRequestCategory($PullRequest) {
    $labels = @($PullRequest.labels | ForEach-Object { [string]$_.name })
    if ($labels -contains 'skip-changelog' -or $labels -contains 'type: skip-changelog') { return $null }
    if ($labels -contains 'breaking' -or $labels -contains 'type: breaking') { return 'breaking' }
    foreach ($candidate in @('feature', 'improvement', 'fix', 'breaking', 'docs')) {
        if ($labels -contains $candidate -or $labels -contains "type: $candidate") { return $candidate }
    }

    $title = [string]$PullRequest.title
    if ($title -match '^[a-z]+!:' -or $title -match '^\w+\([^)]*\)!:') { return 'breaking' }
    if ($title -match '^feat(\(|:)') { return 'feature' }
    if ($title -match '^perf(\(|:)') { return 'improvement' }
    if ($title -match '^fix(\(|:)') { return 'fix' }
    if ($title -match '^refactor(\(|:)') { return 'improvement' }
    if ($title -match '^(docs|build|ci|test|chore|revert)(\(|!|:)') { return 'other' }
    return 'other'
}

function Get-PullRequestLogin($PullRequest) {
    if ($PullRequest.author.login) { return [string]$PullRequest.author.login }
    if ($PullRequest.user.login) { return [string]$PullRequest.user.login }
    return 'contributor'
}

function Get-PRNote([string]$Section, $PullRequest) {
    $lines = Get-ChangelogLines $Section
    $number = if ($PullRequest.number) { " (#$($PullRequest.number))" } else { '' }
    $login = Get-PullRequestLogin $PullRequest
    return ($lines | ForEach-Object { "$_$number @$login" })
}

switch ($Mode) {
    'ValidateCommit' {
        Require-Value $Subject 'Subject'
        $pattern = '^(feat|fix|perf|refactor|docs|build|ci|test|chore|revert)(\([a-zA-Z0-9._-]+\))?!?: .+'
        if ($Subject -notmatch $pattern) {
            throw "Commit subject does not follow Conventional Commits: $Subject"
        }
    }
    'ValidateVersion' {
        Require-Value $Tag 'Tag'
        $version = Get-TagReleaseVersion $Tag
        if ($PreviousTag) {
            $previousVersion = Get-TagReleaseVersion $PreviousTag 'Previous tag'
            if ((Compare-ReleaseVersion $version $previousVersion) -le 0) {
                throw "Release version $($version.Text) must be greater than $($previousVersion.Text)."
            }
        }
    }
    'ValidateNotes' {
        $body = Read-RequiredFile $BodyPath 'BodyPath'
        [void](Get-ValidatedNotes $body)
    }
    'ValidateFeature' {
        $body = Read-RequiredFile $BodyPath 'BodyPath'
        if ($Category -notin @('feature', 'improvement', 'fix', 'breaking', 'docs', 'skip-changelog', 'other')) {
            throw "Unsupported release category: $Category"
        }
        if ($Category -ne 'skip-changelog') {
            [void](Get-ValidatedNotes $body)
        }
    }
    'ValidateReleasePr' {
        $body = Read-RequiredFile $BodyPath 'BodyPath'
        if ($body -match '(?im)TODO|待填写') {
            throw 'Release PR still contains a template placeholder.'
        }
        foreach ($required in @('发布版本', 'Release version', '发布范围', 'Included changes', '发布前验证', 'Release validation')) {
            if ($body -notmatch [regex]::Escape($required)) {
                throw "Release PR is missing required section: $required"
            }
        }
    }
    'SelectReleasePr' {
        Require-Value $CommitSha 'CommitSha'
        Require-Value $OutputPath 'OutputPath'
        $pullRequestsJson = Read-RequiredFile $PullRequestsPath 'PullRequestsPath'
        $pullRequests = @($pullRequestsJson | ConvertFrom-Json)
        $matches = @($pullRequests | Where-Object {
            $_.merged_at -and
            $_.merge_commit_sha -eq $CommitSha -and
            $_.base.ref -eq 'master' -and
            $_.head.ref -eq 'dev'
        })
        if ($matches.Count -ne 1) {
            throw "Tagged commit must be associated with exactly one merged dev -> master pull request; found $($matches.Count)."
        }

        if ([string]::IsNullOrWhiteSpace([string]$matches[0].body)) {
            throw 'The release pull request body is empty.'
        }

        [System.IO.File]::WriteAllText($OutputPath, [string]$matches[0].body, [System.Text.UTF8Encoding]::new($false))
    }
    'Compose' {
        Require-Value $OutputPath 'OutputPath'
        Require-Value $Tag 'Tag'
        Require-Value $RepositoryUrl 'RepositoryUrl'
        & $PSCommandPath -Mode ValidateVersion -Tag $Tag -PreviousTag $PreviousTag

        $body = Read-RequiredFile $BodyPath 'BodyPath'
        $notes = Get-ValidatedNotes $body
        $changelogUrl = if ($PreviousTag) {
            "$RepositoryUrl/compare/$PreviousTag...$Tag"
        } else {
            "$RepositoryUrl/commits/$Tag"
        }
        $releaseBody = @"
## 更新内容

$($notesMarkers.ChineseStart)
$($notes.Chinese)
$($notesMarkers.ChineseEnd)

## What's Changed

$($notesMarkers.EnglishStart)
$($notes.English)
$($notesMarkers.EnglishEnd)

## Full Changelog

$changelogUrl
"@
        [System.IO.File]::WriteAllText($OutputPath, $releaseBody.Trim() + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    }
    'ComposePullRequests' {
        Require-Value $PullRequestsPath 'PullRequestsPath'
        Require-Value $OutputPath 'OutputPath'
        Require-Value $Tag 'Tag'
        Require-Value $RepositoryUrl 'RepositoryUrl'
        & $PSCommandPath -Mode ValidateVersion -Tag $Tag -PreviousTag $PreviousTag

        $pullRequests = @((Read-RequiredFile $PullRequestsPath 'PullRequestsPath') | ConvertFrom-Json)
        $groups = [ordered]@{
            feature = @{ Chinese = [System.Collections.Generic.List[string]]::new(); English = [System.Collections.Generic.List[string]]::new() }
            improvement = @{ Chinese = [System.Collections.Generic.List[string]]::new(); English = [System.Collections.Generic.List[string]]::new() }
            fix = @{ Chinese = [System.Collections.Generic.List[string]]::new(); English = [System.Collections.Generic.List[string]]::new() }
            breaking = @{ Chinese = [System.Collections.Generic.List[string]]::new(); English = [System.Collections.Generic.List[string]]::new() }
            other = @{ Chinese = [System.Collections.Generic.List[string]]::new(); English = [System.Collections.Generic.List[string]]::new() }
        }
        $contributors = [System.Collections.Generic.List[string]]::new()
        $seenNumbers = [System.Collections.Generic.HashSet[string]]::new()

        foreach ($pullRequest in $pullRequests | Sort-Object { [int]$_.number }) {
            $category = Get-PullRequestCategory $pullRequest
            if ($null -eq $category) { continue }
            $key = if ($category -eq 'docs') { 'other' } else { $category }
            $number = [string]$pullRequest.number
            if ($number -and -not $seenNumbers.Add($number)) { continue }

            $notes = Get-ValidatedNotes ([string]$pullRequest.body)
            foreach ($line in Get-PRNote $notes.Chinese $pullRequest) { $groups[$key].Chinese.Add($line) }
            foreach ($line in Get-PRNote $notes.English $pullRequest) { $groups[$key].English.Add($line) }
            $login = Get-PullRequestLogin $pullRequest
            if (-not $contributors.Contains($login)) { $contributors.Add($login) }
        }

        $labels = @{
            feature = @('新功能', 'Features')
            improvement = @('改进', 'Improvements')
            fix = @('修复', 'Bug Fixes')
            breaking = @('破坏性变更', 'Breaking Changes')
            other = @('其他变更', 'Other Changes')
        }
        $zh = [System.Collections.Generic.List[string]]::new()
        $en = [System.Collections.Generic.List[string]]::new()
        foreach ($key in $groups.Keys) {
            $zhItems = @($groups[$key].Chinese)
            $enItems = @($groups[$key].English)
            if ($zhItems.Count -eq 0 -and $enItems.Count -eq 0) { continue }
            $zh.Add("### $($labels[$key][0])")
            $en.Add("### $($labels[$key][1])")
            foreach ($item in $zhItems) { $zh.Add("- $item") }
            foreach ($item in $enItems) { $en.Add("- $item") }
        }
        if ($zh.Count -eq 0) {
            $zh.Add('### 其他变更'); $zh.Add('- 本次版本没有可公开汇总的功能变更。')
            $en.Add('### Other Changes'); $en.Add('- No user-facing changes were selected for this release.')
        }

        $changelogUrl = if ($PreviousTag) { "$RepositoryUrl/compare/$PreviousTag...$Tag" } else { "$RepositoryUrl/commits/$Tag" }
        $releaseBody = @"
## 更新内容

$($notesMarkers.ChineseStart)
$($zh -join "`n")
$($notesMarkers.ChineseEnd)

## What's Changed

$($notesMarkers.EnglishStart)
$($en -join "`n")
$($notesMarkers.EnglishEnd)

## Full Changelog

$changelogUrl

## Contributors

$(if ($contributors.Count -gt 0) { ($contributors | ForEach-Object { "- @$_" }) -join "`n" } else { '- None' })
"@
        [System.IO.File]::WriteAllText($OutputPath, $releaseBody.Trim() + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    }
}

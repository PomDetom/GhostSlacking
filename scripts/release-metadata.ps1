param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('ValidateCommit', 'ValidateVersion', 'ValidateNotes', 'SelectReleasePr', 'Compose')]
    [string]$Mode,
    [string]$Subject,
    [string]$BodyPath,
    [string]$PullRequestsPath,
    [string]$CommitSha,
    [string]$OutputPath,
    [string]$Tag,
    [string]$PreviousTag,
    [string]$RepositoryUrl
)

$ErrorActionPreference = 'Stop'
$notesMarkers = @{
    ChineseStart = '<!-- release-notes:zh:start -->'
    ChineseEnd = '<!-- release-notes:zh:end -->'
    EnglishStart = '<!-- release-notes:en:start -->'
    EnglishEnd = '<!-- release-notes:en:end -->'
}

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
        if ($Tag -notmatch '^v(\d+\.\d+\.\d+)$') {
            throw "Release tag is invalid: $Tag"
        }

        $version = [version]$Matches[1]
        if ($PreviousTag) {
            if ($PreviousTag -notmatch '^v(\d+\.\d+\.\d+)$') {
                throw "Previous release tag is invalid: $PreviousTag"
            }

            $previousVersion = [version]$Matches[1]
            if ($version -le $previousVersion) {
                throw "Release version $version must be greater than $previousVersion."
            }
        }
    }
    'ValidateNotes' {
        $body = Read-RequiredFile $BodyPath 'BodyPath'
        [void](Get-ValidatedNotes $body)
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
}

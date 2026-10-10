[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $SkipBuild) { & (Join-Path $root 'installer/build-native-helper.ps1') -Testing | Out-Host }
$helper = Join-Path $root 'artifacts/native-helper-tests/GhostSlacking.InstallHelper.exe'
$testRoot = Join-Path $root "artifacts/tests/installer-$([guid]::NewGuid().ToString('N')) 中文 space"
$registry = 'HKCU:\Software\GhostSlacking.InstallerTests'
if (Test-Path -LiteralPath $registry) { throw 'An installer test registration already exists; clean up the earlier test before running.' }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
function Invoke-Helper([string[]]$Arguments, [int]$Expected = 0) {
    & $helper @Arguments | Out-Host
    if ($LASTEXITCODE -ne $Expected) { throw "Installer helper failed: $Arguments (expected $Expected, got $LASTEXITCODE)." }
}
function Prepare-Payload([string]$Version) {
    Invoke-Helper @('stage', $testRoot)
    [IO.File]::WriteAllText((Join-Path $testRoot '.staging/GhostSlacking.App.exe'), $Version)
    [IO.File]::WriteAllText((Join-Path $testRoot 'uninstall-new.exe'), "uninstall $Version")
}
function Assert-Version([string]$Version) {
    if ([IO.File]::ReadAllText((Join-Path $testRoot 'app/GhostSlacking.App.exe')) -ne $Version -or
        (Get-ItemProperty -LiteralPath $registry).InstallerVersion -ne $Version) { throw "Expected installed version $Version." }
}
try {
    Invoke-Helper @('validate', $testRoot)
    $unrelated = Join-Path $testRoot 'keep.txt'
    [IO.File]::WriteAllText($unrelated, 'unrelated data')
    Invoke-Helper @('stage', $testRoot) 20
    if ([IO.File]::ReadAllText($unrelated) -ne 'unrelated data') { throw 'Staging changed an unrelated directory.' }
    Remove-Item -LiteralPath $unrelated
    Prepare-Payload '1.0.0-beta.1'
    Invoke-Helper @('install', $testRoot, '1.0.0-beta.1', '1', '0')
    Assert-Version '1.0.0-beta.1'
    $env:GHOSTSLACKING_TEST_FAIL_SWITCH = '1'
    Prepare-Payload '1.0.0-beta.2'
    Invoke-Helper @('install', $testRoot, '1.0.0-beta.2', '1', '0') 20
    Remove-Item Env:GHOSTSLACKING_TEST_FAIL_SWITCH
    Assert-Version '1.0.0-beta.1'
    $env:GHOSTSLACKING_TEST_CRASH_SWITCH = '1'
    Prepare-Payload '1.0.0-beta.2'
    Invoke-Helper @('install', $testRoot, '1.0.0-beta.2', '1', '0') 99
    Remove-Item Env:GHOSTSLACKING_TEST_CRASH_SWITCH
    Invoke-Helper @('recover', $testRoot)
    Assert-Version '1.0.0-beta.1'
    Prepare-Payload '1.0.0-rc.1'
    Invoke-Helper @('install', $testRoot, '1.0.0-rc.1', '1', '0')
    Assert-Version '1.0.0-rc.1'
    $digest = (& $helper id $testRoot).Trim()
    $mutex = [Threading.Mutex]::new($false, "Local\GhostSlacking.Installation.$digest")
    if (-not $mutex.WaitOne(0)) { throw 'Could not acquire the concurrency test lock.' }
    try { Invoke-Helper @('stage', $testRoot) 20 }
    finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
    Assert-Version '1.0.0-rc.1'
    if (-not (Test-Path -LiteralPath (Join-Path $testRoot 'previous/GhostSlacking.App.exe'))) { throw 'Previous program backup was not retained.' }
    Invoke-Helper @('confirm', $testRoot)
    if (Test-Path -LiteralPath (Join-Path $testRoot 'previous')) { throw 'Startup confirmation did not remove the program backup.' }
    # A file opened without delete-sharing must block switching and preserve registration.
    Prepare-Payload '1.0.0'
    $locked = [IO.File]::Open((Join-Path $testRoot 'app/GhostSlacking.App.exe'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { Invoke-Helper @('install', $testRoot, '1.0.0', '1', '0') 20 }
    finally { $locked.Dispose() }
    Assert-Version '1.0.0-rc.1'
    Invoke-Helper @('install', $testRoot, '1.0.0', '1', '0')
    Assert-Version '1.0.0'
    Prepare-Payload '1.0.0-rc.2'
    Invoke-Helper @('install', $testRoot, '1.0.0-rc.2', '1', '0') 20
    Assert-Version '1.0.0'
    Invoke-Helper @('uninstall', $testRoot)
    if (Test-Path -LiteralPath $registry) { throw 'Uninstall retained its program registration.' }
    if (Test-Path -LiteralPath (Join-Path $testRoot 'app')) { throw 'Uninstall retained program files.' }
    Invoke-Helper @('id', '\\server\share\GhostSlacking') 20
    Write-Output 'Native installer transaction tests passed.'
} finally {
    Remove-Item Env:GHOSTSLACKING_TEST_FAIL_SWITCH,Env:GHOSTSLACKING_TEST_CRASH_SWITCH -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $registry) { & $helper uninstall $testRoot | Out-Host }
    $resolvedTarget = [IO.Path]::GetFullPath($testRoot)
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/tests')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTarget.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to clean a test directory outside artifacts/tests.' }
    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

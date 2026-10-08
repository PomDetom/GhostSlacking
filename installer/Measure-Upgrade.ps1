[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineMsi,
    [Parameter(Mandatory)][string]$SetupExe,
    [ValidateRange(5, 100)][int]$Samples = 5,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\upgrade-measurements'),
    [ValidateSet('warm', 'cold', 'uncontrolled')][string]$Cache = 'uncontrolled'
)
$ErrorActionPreference = 'Stop'
$BaselineMsi = (Resolve-Path -LiteralPath $BaselineMsi).Path
$SetupExe = (Resolve-Path -LiteralPath $SetupExe).Path
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$originalInstall = Get-ItemProperty 'HKLM:\SOFTWARE\GhostSlacking'
$originalDirectory = $originalInstall.InstallLocation
$shutdownRuntime = Join-Path $OutputDirectory 'shutdown-runtime'
New-Item -ItemType Directory -Force -Path $shutdownRuntime | Out-Null
foreach ($assemblyName in @('GhostSlacking.Core.dll', 'GhostSlacking.Platform.dll')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "..\artifacts\setup-publish\win-x64\$assemblyName") -Destination (Join-Path $shutdownRuntime $assemblyName) -Force
    [System.Reflection.Assembly]::LoadFrom((Resolve-Path (Join-Path $shutdownRuntime $assemblyName)).Path) | Out-Null
}
$originalFeatures = @('MainProgramFeature')
if ($originalInstall.StartMenuShortcut -eq 1) { $originalFeatures += 'StartMenuShortcutFeature' }
if ($originalInstall.DesktopShortcut -eq 1) { $originalFeatures += 'DesktopShortcutFeature' }

function Invoke-Installer([string]$File, [string[]]$Arguments, [bool]$Elevate = $false) {
    $info = [System.Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute = $true
    if ($Elevate) { $info.Verb = 'runas' }
    if ($File -eq 'msiexec.exe') {
        # MSI's property grammar requires NAME="value", not "NAME=value".
        $info.Arguments = ($Arguments | ForEach-Object {
            if ($_ -match '^(?<name>[A-Z]+)=(?<value>.*)$') {
                $Matches.name + '="' + $Matches.value.TrimEnd('\') + '"'
            } elseif ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
        }) -join ' '
    } else {
        foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    }
    $process = [System.Diagnostics.Process]::Start($info)
    try {
        $process.WaitForExit()
        if ($process.ExitCode -notin @(0, 1641, 3010)) { throw "Installer failed: $File, exit $($process.ExitCode). Measurement stopped." }
    } finally { $process.Dispose() }
}

function Restore-Baseline {
    [GhostSlacking.Platform.InstallerShutdown]::CloseAndWait($originalDirectory, [TimeSpan]::FromSeconds(30))
    # This fixture operation is for testing only; the product exposes only in-place upgrades.
    $bundles = @(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' |
        Get-ItemProperty | Where-Object { $_.DisplayName -eq 'GhostSlacking' -and $_.BundleCachePath })
    foreach ($bundle in $bundles) {
        Invoke-Installer $bundle.BundleCachePath @('/uninstall', '/passive', '/norestart')
    }
    Invoke-Installer 'msiexec.exe' @('/i', $BaselineMsi, '/passive', '/norestart', "INSTALLFOLDER=$originalDirectory", "ADDLOCAL=$($originalFeatures -join ',')") $true
}

function Read-MsiStages([string]$Path) {
    # Follow only the new product's server thread. Nested old-product actions must
    # not end its RemoveExistingProducts interval prematurely.
    $actionEvents = @([regex]::Matches((Get-Content -LiteralPath $Path -Raw),
        'MSI \(s\) \((?<thread>[0-9A-F]+:[0-9A-F]+)\) \[(?<time>\d{2}:\d{2}:\d{2}:\d{3})\]: Doing action: (?<action>\w+)'))
    if ($actionEvents.Count -eq 0) { throw "No MSI server action timings in $Path" }
    $serverThread = $actionEvents[0].Groups['thread'].Value
    $events = @($actionEvents | Where-Object { $_.Groups['thread'].Value -eq $serverThread })
    $stages = @{
        new_runtime_check_ms = 'Wix4NetFxDotNetCompatibilityCheck_X64'
        file_execute_ms = 'InstallExecute'
        old_product_remove_ms = 'RemoveExistingProducts'
        commit_ms = 'InstallFinalize'
    }
    $timings = [ordered]@{}
    foreach ($stage in $stages.Keys) {
        $total = 0.0
        $found = $false
        for ($index = 0; $index -lt $events.Count - 1; $index++) {
            if ($events[$index].Groups['action'].Value -ne $stages[$stage]) { continue }
            $start = [datetime]::ParseExact($events[$index].Groups['time'].Value, 'HH:mm:ss:fff', [cultureinfo]::InvariantCulture)
            $end = [datetime]::ParseExact($events[$index + 1].Groups['time'].Value, 'HH:mm:ss:fff', [cultureinfo]::InvariantCulture)
            if ($end -lt $start) { $end = $end.AddDays(1) }
            $total += ($end - $start).TotalMilliseconds
            $found = $true
        }
        if (-not $found) { throw "Missing MSI stage $($stages[$stage]) in $Path" }
        $timings[$stage] = $total
    }
    return $timings
}

$results = [System.Collections.Generic.List[object]]::new()
try {
    for ($sample = 1; $sample -le $Samples; $sample++) {
        Restore-Baseline
        $location = (Get-ItemProperty 'HKLM:\SOFTWARE\GhostSlacking').InstallLocation
        $baselineVersion = (Get-Item (Join-Path $location 'GhostSlacking.App.dll')).VersionInfo.ProductVersion
        Start-Process -FilePath (Join-Path $location 'GhostSlacking.App.exe') -WindowStyle Hidden
        # Wait for initialization before measuring shutdown, rather than adding an installation delay.
        $ready = [System.Diagnostics.Stopwatch]::StartNew()
        while (-not (Get-Process GhostSlacking.Watchdog -ErrorAction SilentlyContinue)) {
            if ($ready.Elapsed.TotalSeconds -gt 15) { throw 'Baseline watchdog did not start.' }
            Start-Sleep -Milliseconds 100
        }
        $log = Join-Path (Resolve-Path $OutputDirectory).Path "sample-$sample.log"
        Invoke-Installer $SetupExe @('/passive', '/norestart', '/log', $log, '--diagnostics')
        $text = Get-Content -LiteralPath $log -Raw
        $metrics = [ordered]@{ sample=$sample; baseline=$baselineVersion; cache=$Cache; installerBytes=(Get-Item $SetupExe).Length }
        foreach ($metric in @('safe_close_ms', 'apply_ms', 'package_ms', 'post_uac_ms')) {
            $match = [regex]::Match($text, "$metric=(?<value>\d+(?:\.\d+)?)")
            if (-not $match.Success) { throw "Missing timing $metric in $log" }
            $metrics[$metric] = [double]::Parse($match.Groups['value'].Value, [cultureinfo]::InvariantCulture)
        }
        $msiStages = Read-MsiStages ([System.IO.Path]::ChangeExtension($log, $null) + '_000_GhostSlackingMsi.log')
        foreach ($stage in $msiStages.Keys) { $metrics[$stage] = $msiStages[$stage] }
        $results.Add([pscustomobject]$metrics)
        $results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputDirectory 'samples.json')
        Write-Output "Sample $sample post-UAC: $($metrics.post_uac_ms) ms"
    }
} finally {
    # Preserve the user's old installation after benchmark runs, including its settings and feature selection.
    Restore-Baseline
    $location = (Get-ItemProperty 'HKLM:\SOFTWARE\GhostSlacking').InstallLocation
    Start-Process -FilePath (Join-Path $location 'GhostSlacking.App.exe') -WindowStyle Hidden
}

$summary = [ordered]@{ samples=$results.Count; cache=$Cache; stages=[ordered]@{} }
foreach ($metric in @('safe_close_ms', 'apply_ms', 'package_ms', 'post_uac_ms',
    'new_runtime_check_ms', 'file_execute_ms', 'old_product_remove_ms', 'commit_ms')) {
    $values = @($results | ForEach-Object { $_.$metric } | Sort-Object)
    $middle = [int][Math]::Floor($values.Count / 2)
    $median = if ($values.Count % 2) { $values[$middle] } else { ($values[$middle-1] + $values[$middle]) / 2 }
    $summary.stages[$metric] = @{ median=$median; maximum=$values[-1] }
}
$summary | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputDirectory 'summary.json')
$summary | ConvertTo-Json -Depth 4

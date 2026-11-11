param(
    [ValidateSet('SelfTest','ResearchSelfTest','CdpSelfTest','Inspect','TimeoutCheck')][string]$Mode = 'SelfTest',
    [long]$WindowHandle = 0,
    [ValidateRange(1,60)][int]$TimeoutSeconds = 20,
    [string]$BrowserPath = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    [string]$OutputDirectory = $(if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'BackgroundInteractionProbe.exe')) { $PSScriptRoot } else { Join-Path $PSScriptRoot '../../artifacts/tools/BackgroundInteractionProbe' })
)
$ErrorActionPreference = 'Stop'
$probeExecutable = Join-Path $OutputDirectory 'BackgroundInteractionProbe.exe'
if (!(Test-Path -LiteralPath $probeExecutable)) { throw "Build the probe first: $probeExecutable" }
if ($Mode -eq 'Inspect' -and $WindowHandle -eq 0) { throw 'Inspect requires WindowHandle.' }
$probeArguments = switch ($Mode) {
    SelfTest { @('--self-test') }
    ResearchSelfTest { @('--research-self-test') }
    CdpSelfTest {
        if (!(Test-Path -LiteralPath $BrowserPath)) { throw "Browser not found: $BrowserPath" }
        $probeProfile = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('cdp-profile-' + [guid]::NewGuid().ToString('N'))
        @('--cdp-self-test', ('"' + $BrowserPath + '"'), ('"' + $probeProfile + '"'))
    }
    Inspect { @('--inspect', "$WindowHandle") }
    TimeoutCheck { @('--hang-test') }
}
$probeOutput = Join-Path $OutputDirectory "probe-$($Mode.ToLowerInvariant()).jsonl"
$probeErrors = Join-Path $OutputDirectory "probe-$($Mode.ToLowerInvariant()).stderr.txt"
$probeProcess = Start-Process -FilePath $probeExecutable -ArgumentList $probeArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $probeOutput -RedirectStandardError $probeErrors
if (!$probeProcess.WaitForExit($TimeoutSeconds * 1000)) {
    # Only this helper, launched above, is terminated. Never App/Watchdog/target.
    $probeProcess.Kill($true)
    $probeProcess.WaitForExit()
    if ($Mode -eq 'TimeoutCheck') { Write-Output 'PASS: isolated helper terminated at its deadline.'; exit 0 }
    throw "Probe deadline exceeded; issued actions may have an uncertain result. See $probeOutput"
}
$probeProcess.Refresh()
Get-Content -LiteralPath $probeOutput
if ($probeProcess.ExitCode -ne 0) { Get-Content -LiteralPath $probeErrors; throw "Probe failed: $($probeProcess.ExitCode)" }
if ($Mode -eq 'TimeoutCheck') { throw 'Hang-test unexpectedly returned.' }

#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Run-ReleaseTests.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("GhostSlacking desktop tests $([char]0x6D4B) $([guid]::NewGuid().ToString('N'))")
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$fixture = Join-Path $testRoot 'fixture with spaces.ps1'
$shell = (Get-Command pwsh -ErrorAction Stop).Source

@'
param([string]$ArgumentsPath, [int]$ExitCode)
$Values = @($args)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DesktopIdentity {
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern bool GetUserObjectInformationW(IntPtr handle,int index,StringBuilder value,int bytes,out int needed);
}
"@
$name = [Text.StringBuilder]::new(256)
$needed = 0
$desktop = [DesktopIdentity]::GetThreadDesktop([DesktopIdentity]::GetCurrentThreadId())
if (-not [DesktopIdentity]::GetUserObjectInformationW($desktop, 2, $name, 512, [ref]$needed)) {
    throw 'Could not read test desktop identity.'
}
if (-not $name.ToString().StartsWith('GhostSlacking.Tests.')) { throw "Unexpected desktop: $name" }
[IO.File]::WriteAllText($ArgumentsPath, (ConvertTo-Json -InputObject @($Values) -Compress))
Write-Output "Owned desktop: $name"
[Console]::Error.WriteLine('fixture stderr')
exit $ExitCode
'@ | Set-Content -LiteralPath $fixture -Encoding utf8

try {
    $values = @('two words', 'embedded "quote"', 'trailing slash\', '')
    foreach ($expected in @(0, 23)) {
        $argumentsPath = Join-Path $testRoot "arguments-$expected.json"
        $logPath = Join-Path $testRoot "output-$expected.log"
        $arguments = @('-NoProfile', '-File', $fixture, '-ArgumentsPath', $argumentsPath, '-ExitCode', "$expected") + $values
        $actual = Invoke-TestDesktopProcess -FilePath $shell -ArgumentList $arguments -WorkingDirectory $testRoot -LogPath $logPath
        if ($actual -ne $expected) { throw "Exit code was $actual, expected $expected." }
        $received = @(Get-Content -LiteralPath $argumentsPath -Raw | ConvertFrom-Json)
        if ($received.Count -ne $values.Count) { throw "Argument count was $($received.Count), expected $($values.Count)." }
        for ($index = 0; $index -lt $values.Count; $index++) {
            if ($received[$index] -cne $values[$index]) { throw "Argument $index was not preserved." }
        }
        $log = Get-Content -LiteralPath $logPath -Raw
        if ($log -notmatch 'Owned desktop: GhostSlacking.Tests\.' -or $log -notmatch 'fixture stderr') {
            throw 'Child stdout/stderr was not captured.'
        }
    }
    Write-Host 'Test desktop runner checks passed.'
}
finally {
    # Only remove the disposable directory this script created.
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

#requires -Version 7.0
param(
    [string]$BaseOutputPath,
    [string]$ResultsDirectory,
    [string]$Filter,
    [ValidateRange(1, 100)][int]$Repeat = 1,
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

if (-not ('GhostSlacking.TestDesktopNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GhostSlacking {
    public static class TestDesktopNative {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct StartupInfo {
            public int Size;
            public string Reserved, Desktop, Title;
            public int X, Y, Width, Height, Columns, Rows, Fill, Flags;
            public short Show, ReservedSize;
            public IntPtr ReservedPointer, Input, Output, Error;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInfo {
            public IntPtr Process, Thread;
            public int ProcessId, ThreadId;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr attributes);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes,
            IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory,
            ref StartupInfo startup, out ProcessInfo process);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        public static void Check(bool success) {
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
'@
}

function ConvertTo-NativeArgument([AllowEmptyString()][string]$Value) {
    # Windows command-line quoting, including quotes and trailing backslashes.
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    '"' + [regex]::Replace($escaped, '(\\+)$', '$1$1') + '"'
}

function Invoke-TestDesktopProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogPath
    )

    # No SWITCHDESKTOP access and no SwitchDesktop call: the user's desktop stays active.
    $desktopName = 'GhostSlacking.Tests.' + [guid]::NewGuid().ToString('N')
    $desktop = [GhostSlacking.TestDesktopNative]::CreateDesktopW($desktopName, 0, 0, 0, 0xC3, 0)
    [GhostSlacking.TestDesktopNative]::Check($desktop -ne [IntPtr]::Zero)
    $process = [GhostSlacking.TestDesktopNative+ProcessInfo]::new()
    $writer = $null
    $inputStream = $null
    $reader = $null
    try {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($LogPath)) | Out-Null
        $writer = [IO.File]::Open($LogPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
        $inputStream = [IO.File]::Open('NUL', [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $outputHandle = $writer.SafeFileHandle.DangerousGetHandle()
        $inputHandle = $inputStream.SafeFileHandle.DangerousGetHandle()
        [GhostSlacking.TestDesktopNative]::Check([GhostSlacking.TestDesktopNative]::SetHandleInformation($outputHandle, 1, 1))
        [GhostSlacking.TestDesktopNative]::Check([GhostSlacking.TestDesktopNative]::SetHandleInformation($inputHandle, 1, 1))

        $startup = [GhostSlacking.TestDesktopNative+StartupInfo]::new()
        $startup.Size = [Runtime.InteropServices.Marshal]::SizeOf($startup)
        $startup.Desktop = $desktopName
        $startup.Flags = 0x100 # STARTF_USESTDHANDLES
        $startup.Input = $inputHandle
        $startup.Output = $outputHandle
        $startup.Error = $outputHandle
        $quoted = @(@($FilePath) + $ArgumentList | ForEach-Object { ConvertTo-NativeArgument $_ })
        $command = [Text.StringBuilder]::new($quoted -join ' ')
        [GhostSlacking.TestDesktopNative]::Check([GhostSlacking.TestDesktopNative]::CreateProcessW(
            $FilePath, $command, 0, 0, $true, 0x08000000, 0, $WorkingDirectory, [ref]$startup, [ref]$process))
        [GhostSlacking.TestDesktopNative]::Check([GhostSlacking.TestDesktopNative]::SetHandleInformation($outputHandle, 1, 0))
        [GhostSlacking.TestDesktopNative]::Check([GhostSlacking.TestDesktopNative]::SetHandleInformation($inputHandle, 1, 0))
        $reader = [IO.StreamReader]::new([IO.File]::Open($LogPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite))
        $elapsed = [Diagnostics.Stopwatch]::StartNew()
        Write-Host "Running tests on $desktopName. Output: $LogPath"
        while ($true) {
            $wait = [GhostSlacking.TestDesktopNative]::WaitForSingleObject($process.Process, 500)
            $text = $reader.ReadToEnd()
            if ($text.Length -gt 0) { [Console]::Out.Write($text) }
            if ($wait -eq 0) { break }
            [GhostSlacking.TestDesktopNative]::Check($wait -eq 258)
            # This is the process budget; the UI fixture keeps its own 60-second limit.
            if ($elapsed.Elapsed.TotalSeconds -gt 180) {
                throw "Test process $($process.ProcessId) exceeded 180 seconds. No processes were terminated. Output: $LogPath"
            }
        }
        [uint32]$code = 0
        [GhostSlacking.TestDesktopNative]::Check([GhostSlacking.TestDesktopNative]::GetExitCodeProcess($process.Process, [ref]$code))
        return [int]$code
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
        if ($null -ne $inputStream) { $inputStream.Dispose() }
        if ($null -ne $writer) { $writer.Dispose() }
        if ($process.Thread -ne [IntPtr]::Zero) { [GhostSlacking.TestDesktopNative]::CloseHandle($process.Thread) | Out-Null }
        if ($process.Process -ne [IntPtr]::Zero) { [GhostSlacking.TestDesktopNative]::CloseHandle($process.Process) | Out-Null }
        [GhostSlacking.TestDesktopNative]::Check([GhostSlacking.TestDesktopNative]::CloseDesktop($desktop))
    }
}

# Tests dot-source only the launcher to exercise desktop isolation and failure propagation.
if ($MyInvocation.InvocationName -eq '.') { return }
if (-not $IsWindows) { throw 'The Release suite requires Windows.' }
$solution = Join-Path $repositoryRoot 'GhostSlacking.sln'
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$outputArguments = @()
if ($BaseOutputPath) { $outputArguments = @("-p:BaseOutputPath=$BaseOutputPath") }
if (-not $NoBuild) {
    $buildArguments = @('build', $solution, '--configuration', 'Release') + $outputArguments
    if ($NoRestore) { $buildArguments += '--no-restore' }
    & $dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if (-not $ResultsDirectory) {
    $ResultsDirectory = 'artifacts/tests/release-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N')
}
if (-not [IO.Path]::IsPathRooted($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $repositoryRoot $ResultsDirectory
}
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
$failed = $false
foreach ($iteration in 1..$Repeat) {
    $runDirectory = Join-Path $ResultsDirectory "run-$iteration"
    [IO.Directory]::CreateDirectory($runDirectory) | Out-Null
    $arguments = @('test', $solution, '--configuration', 'Release', '--no-build', '--no-restore',
        '--results-directory', $runDirectory, '--logger', 'trx') + $outputArguments
    if ($Filter) { $arguments += @('--filter', $Filter) }
    $code = Invoke-TestDesktopProcess -FilePath $dotnet -ArgumentList $arguments -WorkingDirectory $repositoryRoot `
        -LogPath (Join-Path $runDirectory 'test-output.log')
    if ($code -ne 0) { $failed = $true }
}
if ($failed) { exit 1 }

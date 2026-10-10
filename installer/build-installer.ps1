[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.5',

    [ValidatePattern('^\d+\.\d+\.\d+(?:-(?:alpha|beta|rc)\.\d+)?$')]
    [string]$InformationalVersion
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $repositoryRoot 'src\GhostSlacking.App\GhostSlacking.App.csproj'
$updaterProject = Join-Path $repositoryRoot 'src\GhostSlacking.Updater\GhostSlacking.Updater.csproj'
$publishRoot = Join-Path $repositoryRoot 'artifacts\installer-publish'
$publishDirectory = Join-Path $publishRoot 'win-x64'
$publishWork = Join-Path $repositoryRoot 'artifacts\installer-publish-work'
$assemblyVersion = "$Version.0"
if ([string]::IsNullOrWhiteSpace($InformationalVersion)) {
    $InformationalVersion = $Version
}
elseif ($InformationalVersion.Split('-')[0] -ne $Version) {
    throw "InformationalVersion must have the same base version as Version ($Version)."
}

$prereleaseMatch = [regex]::Match($InformationalVersion, '-(?<channel>alpha|beta|rc)\.(?<number>\d+)$')
$fileRevision = 65535
if ($prereleaseMatch.Success) {
    try {
        $prereleaseNumber = [long]::Parse(
            $prereleaseMatch.Groups['number'].Value,
            [System.Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        throw "The prerelease number in InformationalVersion is too large for a Windows file version: $InformationalVersion."
    }

    $revisionOffset, $maximumPrereleaseNumber = switch ($prereleaseMatch.Groups['channel'].Value) {
        'alpha' { 1, 16382; break }
        'beta'  { 16384, 16382; break }
        'rc'    { 32768, 32766; break }
    }
    if ($prereleaseNumber -gt $maximumPrereleaseNumber) {
        throw "The prerelease number in InformationalVersion exceeds the file-version range for $($prereleaseMatch.Groups['channel'].Value): $InformationalVersion."
    }

    $fileRevision = $revisionOffset + $prereleaseNumber
}
$fileVersion = "$Version.$fileRevision"

$installerPath = Join-Path $repositoryRoot "artifacts/installer/GhostSlacking-$InformationalVersion-win-x64-setup.exe"
$compiler = & (Join-Path $PSScriptRoot 'get-nsis.ps1')
$helper = & (Join-Path $PSScriptRoot 'build-native-helper.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Native helper build failed.' }
New-Item -ItemType Directory -Path (Split-Path $installerPath) -Force | Out-Null
# Both subordinate processes remain framework dependent and carry their own runtime configs.
dotnet restore $updaterProject --runtime win-x64 --artifacts-path $publishWork
if ($LASTEXITCODE -ne 0) { throw 'Updater restore failed.' }
if (Test-Path -LiteralPath $publishDirectory) {
    $resolvedTarget = [IO.Path]::GetFullPath($publishDirectory)
    $allowedRoot = [IO.Path]::GetFullPath($publishRoot) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTarget.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish output escaped the artifacts directory.' }
    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}
dotnet publish $appProject --configuration Release --runtime win-x64 --self-contained false `
    --artifacts-path $publishWork --output $publishDirectory `
    -p:Version=$Version -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$fileVersion `
    -p:InformationalVersion=$InformationalVersion -p:PublishTrimmed=false -p:DebugSymbols=false -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw 'Application publication failed.' }
Copy-Item -LiteralPath $helper.FullName -Destination (Join-Path $publishDirectory 'GhostSlacking.InstallHelper.exe') -Force
foreach ($relative in @('GhostSlacking.App', 'GhostSlacking.Watchdog', 'Updater/GhostSlacking.Updater')) {
    $config = Get-Content -LiteralPath (Join-Path $publishDirectory "$relative.runtimeconfig.json") -Raw | ConvertFrom-Json
    if ($config.runtimeOptions.framework.name -ne 'Microsoft.NETCore.App' -or $config.runtimeOptions.framework.version -notmatch '^8\.0\.') { throw "Invalid runtime contract: $relative" }
    $dll = Get-Item -LiteralPath (Join-Path $publishDirectory "$relative.dll")
    if ($dll.VersionInfo.FileVersion -ne $fileVersion) { throw "Invalid file version: $relative" }
    if ($dll.VersionInfo.ProductVersion.Split('+')[0] -ne $InformationalVersion) { throw "Invalid full release version: $relative" }
}
if (Get-ChildItem -LiteralPath $publishDirectory -Filter coreclr.dll -Recurse) { throw 'The installer must remain framework dependent.' }
& $compiler /V2 "/DDISPLAY_VERSION=$InformationalVersion" "/DFILE_VERSION=$fileVersion" `
    "/DOUTPUT_FILE=$installerPath" "/DPUBLISH_DIRECTORY=$publishDirectory" `
    "/DHELPER_FILE=$($helper.FullName)" "/DAPP_ICON=$(Join-Path $repositoryRoot 'src\GhostSlacking.App\Assets\GhostSlacking.ico')" `
    (Join-Path $PSScriptRoot 'GhostSlacking.nsi')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installerPath)) { throw 'NSIS compilation failed.' }
Write-Output "Current-user EXE created: $installerPath"

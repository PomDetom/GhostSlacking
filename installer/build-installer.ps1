[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $repositoryRoot 'src\GhostSlacking.App\GhostSlacking.App.csproj'
$installerProject = Join-Path $PSScriptRoot 'GhostSlacking.Installer.wixproj'
$publishDirectory = Join-Path $repositoryRoot 'artifacts\installer-publish\win-x64'
$publishWork = Join-Path $repositoryRoot 'artifacts\installer-publish-work'
$installerPath = Join-Path $repositoryRoot "artifacts\installer\GhostSlacking-$Version-win-x64.msi"

dotnet publish $appProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --artifacts-path $publishWork `
    --output $publishDirectory `
    -p:PublishTrimmed=false `
    -p:DebugSymbols=false `
    -p:DebugType=None
if ($LASTEXITCODE -ne 0) {
    throw "GhostSlacking publish failed with exit code $LASTEXITCODE."
}

dotnet build $installerProject `
    --configuration Release `
    -p:ProductVersion=$Version `
    -p:PublishDirectory=$publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "GhostSlacking installer build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer output was not found at $installerPath."
}

Get-Item -LiteralPath $installerPath

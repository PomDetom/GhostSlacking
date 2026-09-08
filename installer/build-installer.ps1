[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $repositoryRoot 'src\GhostSlacking.App\GhostSlacking.App.csproj'
$installerProject = Join-Path $PSScriptRoot 'GhostSlacking.Installer.wixproj'
$publishRoot = Join-Path $repositoryRoot 'artifacts\installer-publish'
$publishDirectory = Join-Path $publishRoot 'win-x64'
$publishWork = Join-Path $repositoryRoot 'artifacts\installer-publish-work'
$installerPath = Join-Path $repositoryRoot "artifacts\installer\GhostSlacking-$Version-win-x64.msi"

if (Test-Path -LiteralPath $publishDirectory) {
    $resolvedPublishRoot = [System.IO.Path]::GetFullPath($publishRoot) + [System.IO.Path]::DirectorySeparatorChar
    $resolvedPublishDirectory = (Resolve-Path -LiteralPath $publishDirectory).Path
    if (-not $resolvedPublishDirectory.StartsWith($resolvedPublishRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear publish directory outside $resolvedPublishRoot."
    }

    Remove-Item -LiteralPath $resolvedPublishDirectory -Recurse -Force
}

dotnet publish $appProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --artifacts-path $publishWork `
    --output $publishDirectory `
    -p:PublishTrimmed=false `
    -p:DebugSymbols=false `
    -p:DebugType=None
if ($LASTEXITCODE -ne 0) {
    throw "GhostSlacking publish failed with exit code $LASTEXITCODE."
}

$runtimeConfigPath = Join-Path $publishDirectory 'GhostSlacking.App.runtimeconfig.json'
$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw
if ($runtimeConfig -notmatch '"name"\s*:\s*"Microsoft\.NETCore\.App"' -or
    $runtimeConfig -match 'Microsoft\.WindowsDesktop\.App') {
    throw 'Publish output must depend only on Microsoft.NETCore.App, not Microsoft.WindowsDesktop.App.'
}

$forbiddenRuntimeFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Where-Object {
    $_.Name -match '^(System\.Windows\.Forms|PresentationFramework|PresentationCore|coreclr|clrjit|hostfxr|hostpolicy).*\.dll$'
}
if ($forbiddenRuntimeFiles) {
    $names = ($forbiddenRuntimeFiles.Name | Sort-Object -Unique) -join ', '
    throw "Publish output unexpectedly contains Desktop or self-contained runtime files: $names"
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

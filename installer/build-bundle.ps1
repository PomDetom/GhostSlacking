[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$DisplayVersion,
    [Parameter(Mandatory)][string]$FileVersion,
    [Parameter(Mandatory)][string]$MsiPath
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$AssemblyVersion = "$Version.0"
# The self-contained WPF BA is deliberately separate from the framework-dependent app payload.
$setupDirectory = Join-Path $repositoryRoot 'artifacts\setup-publish\win-x64'
$setupProject = Join-Path $repositoryRoot 'src\GhostSlacking.Setup\GhostSlacking.Setup.csproj'
if (Test-Path -LiteralPath $setupDirectory) {
    $setupRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\setup-publish')) + [System.IO.Path]::DirectorySeparatorChar
    $resolvedSetupDirectory = (Resolve-Path -LiteralPath $setupDirectory).Path
    if (-not $resolvedSetupDirectory.StartsWith($setupRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear setup payload outside $setupRoot."
    }
    Remove-Item -LiteralPath $resolvedSetupDirectory -Recurse -Force
}
dotnet publish $setupProject --configuration Release --runtime win-x64 --self-contained true `
    --output $setupDirectory -p:Version=$Version -p:AssemblyVersion=$AssemblyVersion `
    -p:FileVersion=$FileVersion -p:InformationalVersion=$DisplayVersion -p:IncludeSourceRevisionInInformationalVersion=false
if ($LASTEXITCODE -ne 0) { throw 'Self-contained setup UI publish failed.' }
$setupConfig = Get-Content (Join-Path $setupDirectory 'GhostSlacking.Setup.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $setupConfig.runtimeOptions.includedFrameworks) { throw 'Setup UI must carry its own runtime.' }

$payloadDocument = [xml]'<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment><PayloadGroup Id="SetupPayloads" /></Fragment></Wix>'
$payloadGroup = $payloadDocument.DocumentElement.FirstChild.FirstChild
foreach ($file in Get-ChildItem -LiteralPath $setupDirectory -File -Recurse | Sort-Object FullName) {
    if ($file.Name -eq 'GhostSlacking.Setup.exe' -or $file.Extension -eq '.pdb') { continue }
    $payload = $payloadDocument.CreateElement('Payload', $payloadDocument.DocumentElement.NamespaceURI)
    $payload.SetAttribute('SourceFile', $file.FullName)
    $payload.SetAttribute('Name', [System.IO.Path]::GetRelativePath($setupDirectory, $file.FullName))
    $payloadGroup.AppendChild($payload) | Out-Null
}
$payloadDocument.Save((Join-Path $repositoryRoot 'artifacts\setup-payloads.wxs'))
dotnet build (Join-Path $PSScriptRoot 'GhostSlacking.Bundle.wixproj') --configuration Release `
    -p:BundleVersion=$FileVersion -p:InstallerDisplayVersion=$DisplayVersion `
    -p:MsiPath=$MsiPath -p:SetupDirectory=$setupDirectory
if ($LASTEXITCODE -ne 0) { throw 'Burn bundle build failed.' }
$bundlePath = Join-Path $repositoryRoot "artifacts\installer\GhostSlacking-$DisplayVersion-win-x64-setup.exe"
if ((Get-Item -LiteralPath $bundlePath).Length -gt 256MB) { throw 'Setup exceeds the update protocol 256 MiB limit.' }
& (Join-Path $PSScriptRoot 'Test-Bundle.ps1') -BundlePath $bundlePath -DisplayVersion $DisplayVersion -FileVersion $FileVersion -MsiPath $MsiPath
Get-Item -LiteralPath $bundlePath

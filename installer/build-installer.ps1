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
$fileVersion = "$Version.0"

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
    -p:Version=$Version `
    -p:AssemblyVersion=$fileVersion `
    -p:FileVersion=$fileVersion `
    -p:InformationalVersion=$Version `
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

$versionedApplicationFiles = @(
    'GhostSlacking.App.dll',
    'GhostSlacking.Core.dll',
    'GhostSlacking.Platform.dll',
    'GhostSlacking.Watchdog.dll'
)
foreach ($fileName in $versionedApplicationFiles) {
    $filePath = Join-Path $publishDirectory $fileName
    if (-not (Test-Path -LiteralPath $filePath)) {
        throw "Versioned application file was not published: $filePath"
    }

    $publishedVersion = (Get-Item -LiteralPath $filePath).VersionInfo.FileVersion
    if ($publishedVersion -ne $fileVersion) {
        throw "Published file version mismatch for $fileName. Expected $fileVersion, found $publishedVersion."
    }
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

function Get-MsiRows {
    param(
        [Parameter(Mandatory)]$Database,
        [Parameter(Mandatory)][string]$Table
    )

    $view = $Database.GetType().InvokeMember(
        'OpenView',
        'InvokeMethod',
        $null,
        $Database,
        @("SELECT * FROM ``$Table``"))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $rows = [System.Collections.Generic.List[object]]::new()
    while ($true) {
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $record) {
            break
        }

        $fieldCount = $record.GetType().InvokeMember('FieldCount', 'GetProperty', $null, $record, $null)
        $fields = [string[]]::new($fieldCount)
        for ($index = 1; $index -le $fieldCount; $index++) {
            $fields[$index - 1] = $record.GetType().InvokeMember(
                'StringData',
                'GetProperty',
                $null,
                $record,
                @($index))
        }

        $rows.Add([pscustomobject]@{ Fields = $fields })
    }

    return $rows.ToArray()
}

function Assert-Msi {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw "MSI table validation failed: $Message"
    }
}

$windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
$database = $windowsInstaller.GetType().InvokeMember(
    'OpenDatabase',
    'InvokeMethod',
    $null,
    $windowsInstaller,
    @([string]$installerPath, [int32]0))

$properties = @{}
foreach ($row in Get-MsiRows -Database $database -Table 'Property') {
    $properties[$row.Fields[0]] = $row.Fields[1]
}

Assert-Msi ($properties['UpgradeCode'] -eq '{AB92C57B-7434-401E-91B7-4878B250E0C7}') 'UpgradeCode changed.'
Assert-Msi ($properties['ProductVersion'] -eq $Version) "ProductVersion is not $Version."
Assert-Msi ($properties['ProductCode'] -match '^\{[0-9A-F-]{36}\}$') 'ProductCode was not generated.'
Assert-Msi ($properties['MSIRESTARTMANAGERCONTROL'] -eq 'Disable') 'Restart Manager must remain disabled to prevent forced process termination.'
Assert-Msi ($properties['SecureCustomProperties'] -match 'GHOSTSLACKING_WAS_RUNNING') 'The running-state property is not secured for the elevated transaction.'
Assert-Msi ($properties['SecureCustomProperties'] -match 'GHOSTSLACKING_STILL_RUNNING' -and
    $properties['SecureCustomProperties'] -match 'GHOSTSLACKING_WATCHDOG_STILL_RUNNING') 'The post-close process checks are not secured for the elevated transaction.'
Assert-Msi ($properties['SecureCustomProperties'] -match 'UPGRADE_PROTOCOL_VERSION') 'The upgrade-protocol property is not secured for the elevated transaction.'
Assert-Msi ($properties['WIXUI_EXITDIALOGOPTIONALCHECKBOX'] -eq '1') 'The post-install launch checkbox is not selected by default.'

$fileRows = Get-MsiRows -Database $database -Table 'File'
foreach ($fileName in $versionedApplicationFiles) {
    $fileRow = $fileRows |
        Where-Object { ($_.Fields[2] -split '\|')[-1] -eq $fileName } |
        Select-Object -First 1
    Assert-Msi ($null -ne $fileRow) "$fileName is missing from the File table."
    Assert-Msi ($fileRow.Fields[4] -eq $fileVersion) "$fileName does not carry file version $fileVersion."
}

$appSearchRow = Get-MsiRows -Database $database -Table 'AppSearch' |
    Where-Object { $_.Fields[0] -eq 'UPGRADE_PROTOCOL_VERSION' -and $_.Fields[1] -eq 'UpgradeProtocolVersionSearch' } |
    Select-Object -First 1
Assert-Msi ($null -ne $appSearchRow) 'The upgrade-protocol registry search is missing from AppSearch.'
$protocolLocator = Get-MsiRows -Database $database -Table 'RegLocator' |
    Where-Object {
        $_.Fields[0] -eq 'UpgradeProtocolVersionSearch' -and
        $_.Fields[2] -eq 'Software\GhostSlacking' -and
        $_.Fields[3] -eq 'UpgradeProtocolVersion'
    } |
    Select-Object -First 1
Assert-Msi ($null -ne $protocolLocator) 'The upgrade-protocol registry locator is missing or points to the wrong value.'

$legacyLaunchCondition = Get-MsiRows -Database $database -Table 'LaunchCondition' |
    Where-Object { $_.Fields[0] -eq 'NOT WIX_UPGRADE_DETECTED OR UPGRADE_PROTOCOL_VERSION = "#1"' } |
    Select-Object -First 1
Assert-Msi ($null -ne $legacyLaunchCondition -and
    $legacyLaunchCondition.Fields[1] -match '卸载所有 GhostSlacking 条目') 'The legacy-version uninstall prompt is missing.'

$protocolRegistryRow = Get-MsiRows -Database $database -Table 'Registry' |
    Where-Object {
        $_.Fields[2] -eq 'Software\GhostSlacking' -and
        $_.Fields[3] -eq 'UpgradeProtocolVersion' -and
        $_.Fields[4] -eq '#1'
    } |
    Select-Object -First 1
Assert-Msi ($null -ne $protocolRegistryRow) 'The installed upgrade-protocol marker is missing.'

$upgradeRow = Get-MsiRows -Database $database -Table 'Upgrade' |
    Where-Object { $_.Fields[6] -eq 'WIX_UPGRADE_DETECTED' } |
    Select-Object -First 1
Assert-Msi ($null -ne $upgradeRow) 'The major-upgrade detection row is missing.'
$upgradeAttributes = [int]$upgradeRow.Fields[4]
Assert-Msi (($upgradeAttributes -band 1) -ne 0) 'Feature-state migration is disabled.'
Assert-Msi (($upgradeAttributes -band 512) -ne 0) 'Same-version upgrades are disabled.'
Assert-Msi (($upgradeAttributes -band 4) -eq 0) 'Remove failures are configured to be ignored.'
Assert-Msi ($upgradeRow.Fields[2] -eq $Version) 'The major-upgrade range does not end at ProductVersion.'

$executeRows = Get-MsiRows -Database $database -Table 'InstallExecuteSequence'
$executeByAction = @{}
foreach ($row in $executeRows) {
    $executeByAction[$row.Fields[0]] = $row.Fields
}

$closeAction = "Wix4CloseApplications_$([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToUpperInvariant())"
Assert-Msi ($executeByAction.ContainsKey('InstallInitialize')) 'InstallInitialize is missing.'
Assert-Msi ($executeByAction.ContainsKey('AppSearch')) 'AppSearch is missing.'
Assert-Msi ($executeByAction.ContainsKey('LaunchConditions')) 'LaunchConditions is missing.'
Assert-Msi ($executeByAction.ContainsKey($closeAction)) 'Wix CloseApplication action is missing.'
Assert-Msi ($executeByAction.ContainsKey('BlockUpgradeWhileGhostSlackingIsRunning')) 'The process-still-running upgrade block is missing.'
Assert-Msi ($executeByAction.ContainsKey('RemoveExistingProducts')) 'RemoveExistingProducts is missing.'
Assert-Msi ($executeByAction.ContainsKey('InstallFinalize')) 'InstallFinalize is missing.'
Assert-Msi ($executeByAction.ContainsKey('InstallExecute')) 'InstallExecute is missing.'
Assert-Msi ($executeByAction.ContainsKey('SetWixUnelevatedShellExecTarget')) 'The upgrade restart target action is missing.'
Assert-Msi ($executeByAction.ContainsKey('RestartGhostSlackingAfterUpgrade')) 'The post-upgrade restart action is missing.'
$initializeSequence = [int]$executeByAction['InstallInitialize'][2]
$appSearchSequence = [int]$executeByAction['AppSearch'][2]
$launchConditionsSequence = [int]$executeByAction['LaunchConditions'][2]
$closeSequence = [int]$executeByAction[$closeAction][2]
$blockSequence = [int]$executeByAction['BlockUpgradeWhileGhostSlackingIsRunning'][2]
$removeSequence = [int]$executeByAction['RemoveExistingProducts'][2]
$installFilesSequence = [int]$executeByAction['InstallFiles'][2]
$installExecuteSequence = [int]$executeByAction['InstallExecute'][2]
$finalizeSequence = [int]$executeByAction['InstallFinalize'][2]
$restartTargetSequence = [int]$executeByAction['SetWixUnelevatedShellExecTarget'][2]
$restartSequence = [int]$executeByAction['RestartGhostSlackingAfterUpgrade'][2]
Assert-Msi ($initializeSequence -lt $closeSequence -and
    $closeSequence -lt $blockSequence -and
    $blockSequence -lt $installFilesSequence) 'Close and verification do not run before file replacement.'
Assert-Msi ($appSearchSequence -lt $launchConditionsSequence -and
    $launchConditionsSequence -lt $closeSequence) 'The legacy-version gate does not run before CloseApplication.'
Assert-Msi ($executeByAction['BlockUpgradeWhileGhostSlackingIsRunning'][1] -eq
    'GHOSTSLACKING_STILL_RUNNING OR GHOSTSLACKING_WATCHDOG_STILL_RUNNING') 'The upgrade block does not check both application processes.'
Assert-Msi ($installExecuteSequence -lt $removeSequence -and
    $removeSequence -lt $finalizeSequence) 'RemoveExistingProducts is not scheduled after InstallExecute in the rollback transaction.'
Assert-Msi ($finalizeSequence -lt $restartTargetSequence -and
    $restartTargetSequence -lt $restartSequence) 'The application restart target and action are not ordered after transaction commit.'
$restartCondition = $executeByAction['RestartGhostSlackingAfterUpgrade'][1]
Assert-Msi ($restartCondition -match 'WIX_UPGRADE_DETECTED' -and
    $restartCondition -match 'GHOSTSLACKING_WAS_RUNNING' -and
    $restartCondition -match 'UILevel >= 4' -and
    $restartCondition -match 'NOT Installed' -and
    $restartCondition -notmatch 'NOT REMOVE') 'The restart action is not restricted to interactive upgrades of a previously running app, or it is incorrectly affected by optional feature removal.'
Assert-Msi ($executeByAction['SetWixUnelevatedShellExecTarget'][1] -eq $restartCondition) 'The upgrade restart target does not use the restart action condition.'

$closeRows = Get-MsiRows -Database $database -Table 'Wix4CloseApplication'
$runningDetectionRow = $closeRows |
    Where-Object { $_.Fields[0] -eq 'DetectGhostSlackingBeforeUpgrade' } |
    Select-Object -First 1
$closeRow = $closeRows |
    Where-Object { $_.Fields[0] -eq 'CloseGhostSlackingForUpgrade' } |
    Select-Object -First 1
Assert-Msi ($null -ne $runningDetectionRow) 'The pre-close running-state detection row is missing.'
Assert-Msi ($runningDetectionRow.Fields[1] -eq 'GhostSlacking.App.exe' -and
    $runningDetectionRow.Fields[3] -eq 'WIX_UPGRADE_DETECTED') 'The pre-close running-state detection row targets the wrong process or condition.'
Assert-Msi ([int]$runningDetectionRow.Fields[4] -eq 0 -and
    [string]::IsNullOrEmpty($runningDetectionRow.Fields[7])) 'The running-state detection row must not close or terminate the process.'
Assert-Msi ([int]$runningDetectionRow.Fields[5] -eq 1 -and
    $runningDetectionRow.Fields[6] -eq 'GHOSTSLACKING_WAS_RUNNING') 'The running-state property is not captured before shutdown.'
Assert-Msi ($null -ne $closeRow) 'The GhostSlacking CloseApplication row is missing.'
Assert-Msi ($closeRow.Fields[1] -eq 'GhostSlacking.App.exe') 'CloseApplication targets the wrong executable.'
Assert-Msi ($closeRow.Fields[3] -eq 'WIX_UPGRADE_DETECTED') 'CloseApplication is not limited to upgrades.'
Assert-Msi ([int]$closeRow.Fields[4] -eq 5) 'Ordinary and elevated WM_CLOSE messages are not both enabled.'
Assert-Msi ([int]$closeRow.Fields[5] -eq 2 -and
    [string]::IsNullOrEmpty($closeRow.Fields[6])) 'The close row must run after state detection without overwriting the captured state.'
Assert-Msi ([string]::IsNullOrEmpty($closeRow.Fields[7])) 'CloseApplication must never force-terminate the process.'
Assert-Msi ([int]$closeRow.Fields[8] -eq 15000) 'CloseApplication timeout is not 15 seconds.'
$appStoppedRow = $closeRows |
    Where-Object { $_.Fields[0] -eq 'VerifyGhostSlackingStopped' } |
    Select-Object -First 1
$watchdogStoppedRow = $closeRows |
    Where-Object { $_.Fields[0] -eq 'VerifyWatchdogStopped' } |
    Select-Object -First 1
Assert-Msi ($null -ne $appStoppedRow -and
    [int]$appStoppedRow.Fields[5] -eq 3 -and
    $appStoppedRow.Fields[6] -eq 'GHOSTSLACKING_STILL_RUNNING') 'The main-process post-close check is missing or out of order.'
Assert-Msi ($null -ne $watchdogStoppedRow -and
    [int]$watchdogStoppedRow.Fields[5] -eq 4 -and
    $watchdogStoppedRow.Fields[6] -eq 'GHOSTSLACKING_WATCHDOG_STILL_RUNNING') 'The Watchdog post-close check is missing or out of order.'
Assert-Msi (($closeRows | Where-Object { -not [string]::IsNullOrEmpty($_.Fields[7]) }).Count -eq 0) 'A CloseApplication row is configured to force-terminate a process.'

$customActions = Get-MsiRows -Database $database -Table 'CustomAction'
$restartAction = $customActions |
    Where-Object { $_.Fields[0] -eq 'RestartGhostSlackingAfterUpgrade' } |
    Select-Object -First 1
Assert-Msi ($null -ne $restartAction -and $restartAction.Fields[3] -eq 'WixUnelevatedShellExec') 'The restart action is not using non-elevated Shell Execute.'
$postInstallLaunchAction = $customActions |
    Where-Object { $_.Fields[0] -eq 'LaunchGhostSlackingAfterInstall' } |
    Select-Object -First 1
Assert-Msi ($null -ne $postInstallLaunchAction -and
    $postInstallLaunchAction.Fields[3] -eq 'WixUnelevatedShellExec') 'The post-install launch action is not using non-elevated Shell Execute.'
$upgradeBlock = $customActions |
    Where-Object { $_.Fields[0] -eq 'BlockUpgradeWhileGhostSlackingIsRunning' } |
    Select-Object -First 1
Assert-Msi ($null -ne $upgradeBlock -and [int]$upgradeBlock.Fields[1] -eq 19) 'The still-running check does not fail the upgrade before removal.'
$restartTarget = $customActions |
    Where-Object { $_.Fields[0] -eq 'SetWixUnelevatedShellExecTarget' } |
    Select-Object -First 1
Assert-Msi ($null -ne $restartTarget -and
    $restartTarget.Fields[3] -eq '[INSTALLFOLDER]GhostSlacking.App.exe') 'The upgrade restart target is incorrect.'
$upgradeSuccessText = $customActions |
    Where-Object { $_.Fields[0] -eq 'SetWIXUI_EXITDIALOGOPTIONALTEXT' } |
    Select-Object -First 1
Assert-Msi ($null -ne $upgradeSuccessText -and
    $upgradeSuccessText.Fields[3] -match '已成功升级' -and
    $upgradeSuccessText.Fields[3] -match '\[ProductVersion\]') 'The versioned upgrade-success message is missing.'

$featureRows = Get-MsiRows -Database $database -Table 'Feature'
$features = @{}
foreach ($row in $featureRows) {
    $features[$row.Fields[0]] = $row.Fields
}

Assert-Msi ($features.ContainsKey('MainProgramFeature') -and [int]$features['MainProgramFeature'][5] -eq 1) 'MainProgramFeature is missing or not installed by default.'
Assert-Msi ($features.ContainsKey('StartMenuShortcutFeature') -and [int]$features['StartMenuShortcutFeature'][5] -eq 1) 'StartMenuShortcutFeature is missing or not installed by default.'
Assert-Msi ($features.ContainsKey('DesktopShortcutFeature') -and [int]$features['DesktopShortcutFeature'][5] -eq 2) 'DesktopShortcutFeature is missing or installed by default.'

$upgradeNavigation = Get-MsiRows -Database $database -Table 'ControlEvent' |
    Where-Object {
        $_.Fields[0] -eq 'WelcomeDlg' -and
        $_.Fields[1] -eq 'Next' -and
        $_.Fields[3] -eq 'UpgradeReadyDlg' -and
        $_.Fields[4] -eq 'WIX_UPGRADE_DETECTED'
    } |
    Select-Object -First 1
Assert-Msi ($null -ne $upgradeNavigation) 'The upgrade-specific wizard page is not connected.'

$controlEvents = Get-MsiRows -Database $database -Table 'ControlEvent'
$postInstallLaunchEvent = $controlEvents |
    Where-Object {
        $_.Fields[0] -eq 'ExitDialog' -and
        $_.Fields[1] -eq 'Finish' -and
        $_.Fields[2] -eq 'DoAction' -and
        $_.Fields[3] -eq 'LaunchGhostSlackingAfterInstall'
    } |
    Select-Object -First 1
$exitDialogCloseEvent = $controlEvents |
    Where-Object {
        $_.Fields[0] -eq 'ExitDialog' -and
        $_.Fields[1] -eq 'Finish' -and
        $_.Fields[2] -eq 'EndDialog'
    } |
    Select-Object -First 1
$postInstallTargetEvent = $controlEvents |
    Where-Object {
        $_.Fields[0] -eq 'ExitDialog' -and
        $_.Fields[1] -eq 'Finish' -and
        $_.Fields[2] -eq '[WixUnelevatedShellExecTarget]' -and
        $_.Fields[3] -eq '[INSTALLFOLDER]GhostSlacking.App.exe'
    } |
    Select-Object -First 1
Assert-Msi ($null -ne $postInstallLaunchEvent -and
    $postInstallLaunchEvent.Fields[4] -match 'WIXUI_EXITDIALOGOPTIONALCHECKBOX' -and
    $postInstallLaunchEvent.Fields[4] -match 'NOT Installed' -and
    $postInstallLaunchEvent.Fields[4] -match 'NOT WIX_UPGRADE_DETECTED') 'The post-install launch event is not restricted to opted-in fresh installs.'
Assert-Msi ($null -ne $postInstallTargetEvent -and
    [int]$postInstallTargetEvent.Fields[5] -lt [int]$postInstallLaunchEvent.Fields[5]) 'The post-install launch target is not set before the launch action.'
Assert-Msi ($null -ne $exitDialogCloseEvent -and
    [int]$postInstallLaunchEvent.Fields[5] -lt [int]$exitDialogCloseEvent.Fields[5]) 'The application launch event does not run before ExitDialog closes.'

$installUiRows = Get-MsiRows -Database $database -Table 'InstallUISequence'
$checkboxTextAction = $customActions |
    Where-Object { $_.Fields[0] -eq 'SetWIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT' } |
    Select-Object -First 1
$checkboxTextSequence = $installUiRows |
    Where-Object { $_.Fields[0] -eq 'SetWIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT' } |
    Select-Object -First 1
Assert-Msi ($null -ne $checkboxTextAction -and
    $checkboxTextAction.Fields[3] -eq '立即启动 GhostSlacking') 'The post-install launch checkbox text is missing.'
Assert-Msi ($null -ne $checkboxTextSequence -and
    $checkboxTextSequence.Fields[1] -eq 'NOT Installed AND NOT WIX_UPGRADE_DETECTED') 'The launch checkbox is not limited to fresh interactive installs.'

Write-Host 'MSI table validation passed.' -ForegroundColor Green

Get-Item -LiteralPath $installerPath

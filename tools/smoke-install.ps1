# Run on a clean GitHub Actions Windows runner against the installer that will be published.
param(
    [Parameter(Mandatory)][string]$Setup,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'The installer smoke test requires a clean Windows CI runner.'
}

$setupPath = (Get-Item -LiteralPath $Setup).FullName
$installDir = Join-Path $env:LOCALAPPDATA 'WoWBoxer.Offhand'
$registryKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WoWBoxer.Offhand'
$shortcuts = @(
    Join-Path ([Environment]::GetFolderPath('Programs')) 'WoWBoxer Offhand.lnk'
    Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'WoWBoxer Offhand.lnk'
)
foreach ($path in (@($installDir, $registryKey) + $shortcuts)) {
    if (Test-Path -LiteralPath $path) { throw "Installer check requires a fresh install. Already present: $path" }
}

$app = Join-Path $installDir 'current\WoWBoxer.exe'
$cli = Join-Path $installDir 'current\wbx.exe'
$updater = Join-Path $installDir 'Update.exe'
$installer = Start-Process -FilePath $setupPath -ArgumentList '--silent' -Wait -PassThru
if ($installer.ExitCode -ne 0) { throw "Setup exited with code $($installer.ExitCode)." }

try {
    foreach ($path in (@($app, $cli, $updater) + $shortcuts)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Setup did not install $path." }
    }
    $entry = Get-ItemProperty -LiteralPath $registryKey
    if ($entry.DisplayName -cne 'WoWBoxer Offhand' -or $entry.DisplayVersion -cne $Version -or $entry.InstallLocation -ne $installDir) {
        throw "Unexpected Apps entry: '$($entry.DisplayName)' version '$($entry.DisplayVersion)' in '$($entry.InstallLocation)'."
    }
    $shell = New-Object -ComObject WScript.Shell
    foreach ($path in $shortcuts) {
        $target = $shell.CreateShortcut($path).TargetPath
        if ($target -ne $app) { throw "Shortcut '$path' targets '$target'; expected '$app'." }
    }
    $actual = & $cli version
    if ($LASTEXITCODE -ne 0 -or $actual -cne $Version) { throw "Installed CLI reported version '$actual'; expected '$Version'." }
    & "$PSScriptRoot/smoke-start.ps1" -Exe $app
}
finally {
    if (Test-Path -LiteralPath $updater -PathType Leaf) {
        $uninstall = Start-Process -FilePath $updater -ArgumentList '--uninstall', '--silent' -Wait -PassThru
        if ($uninstall.ExitCode -ne 0) { throw "Uninstall exited with code $($uninstall.ExitCode)." }
    }
}
# Velopack schedules deletion of its running Update.exe. The app, shortcuts and Apps entry
# are removed by the uninstaller itself and can be checked as soon as it exits.
foreach ($path in (@($registryKey, $app, $cli) + $shortcuts)) {
    if (Test-Path -LiteralPath $path) { throw "Uninstall left $path in place." }
}
Write-Host "Verified the per-user installation, shortcuts, Apps entry, CLI, main window and uninstall of WoWBoxer Offhand $Version."

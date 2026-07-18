[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$windowsIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$windowsPrincipal = [Security.Principal.WindowsPrincipal]::new($windowsIdentity)
$isAdministrator = $windowsPrincipal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator) {
    Write-Output 'Administrator access is required to remove the development certificate.'
    $elevatedProcess = Start-Process `
        -FilePath 'powershell.exe' `
        -Verb RunAs `
        -ArgumentList @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', "`"$PSCommandPath`""
        ) `
        -Wait `
        -PassThru
    if ($elevatedProcess.ExitCode -ne 0) {
        throw "Elevated removal failed with exit code $($elevatedProcess.ExitCode)."
    }
    return
}

$packageName = 'PetriTuononen.LenovoDesktopFanControl'
$publisher = 'CN=LenovoDesktopFanControl'
$legacyTaskName = 'Lenovo Desktop Fan Control'
$currentUserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskName = "Lenovo Desktop Fan Control - $currentUserSid"
$runKeyPath = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'LenovoDesktopFanControl'

# Stop the hidden owner before unregistering it or deleting its files.
$stopEventName = 'Local\LenovoDesktopFanControl.StopLightingBackground'
$hostMutexName = 'Local\LenovoDesktopFanControl.LightingBackground'
try {
    $stopEvent = [System.Threading.EventWaitHandle]::OpenExisting($stopEventName)
    try {
        [void]$stopEvent.Set()
    }
    finally {
        $stopEvent.Dispose()
    }
}
catch [System.Threading.WaitHandleCannotBeOpenedException] {
    # The background host is not running.
}

try {
    $hostMutex = [System.Threading.Mutex]::OpenExisting($hostMutexName)
    try {
        if (-not $hostMutex.WaitOne([TimeSpan]::FromSeconds(5))) {
            throw 'Timed out waiting for the lighting background host to stop.'
        }
        $hostMutex.ReleaseMutex()
    }
    catch [System.Threading.AbandonedMutexException] {
        # The terminated host no longer owns the application files.
    }
    finally {
        $hostMutex.Dispose()
    }
}
catch [System.Threading.WaitHandleCannotBeOpenedException] {
    # It stopped before the mutex lookup.
}

# Remove both the current scheduled-task registration and the legacy Run-key
# registration used by older releases.
foreach ($scheduledTaskName in @($taskName, $legacyTaskName)) {
    & (Join-Path $env:SystemRoot 'System32\schtasks.exe') /Delete /TN $scheduledTaskName /F | Out-Null
    if ($LASTEXITCODE -notin 0, 1) {
        throw "Could not remove the '$scheduledTaskName' startup task (exit code $LASTEXITCODE)."
    }
}
$runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runKeyPath, $true)
try {
    if ($null -ne $runKey) {
        $runKey.DeleteValue($runValueName, $false)
    }
}
finally {
    if ($null -ne $runKey) {
        $runKey.Dispose()
    }
}

$packages = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue

if ($null -ne $packages) {
    $packages | Remove-AppxPackage
}

Get-ChildItem `
    Cert:\CurrentUser\My,
    Cert:\CurrentUser\TrustedPeople,
    Cert:\LocalMachine\TrustedPeople `
    -ErrorAction SilentlyContinue |
    Where-Object Subject -eq $publisher |
    Remove-Item -Force

Write-Output 'The lighting background host, startup registration, package identity, and development certificate were removed.'

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$stopEventName = 'Local\LenovoDesktopFanControl.StopLightingBackground.Debug'
$hostMutexName = 'Local\LenovoDesktopFanControl.LightingBackground.Debug'
$applicationMutexName = 'Local\LenovoDesktopFanControl.Debug'

# Stopping only the host while its interactive window remains open leaves that
# window unable to control lighting. Refuse the build instead, so the developer
# can close the app cleanly before its loaded assemblies are replaced.
try {
    $applicationMutex = [System.Threading.Mutex]::OpenExisting($applicationMutexName)
    $applicationMutex.Dispose()
    throw 'Close the Lenovo Desktop Fan Control debug window from its tray menu before building. The build did not stop its background lighting host.'
}
catch [System.Threading.WaitHandleCannotBeOpenedException] {
    # No Debug UI owns the build output.
}

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
    exit 0
}

# The host releases this mutex after it has disposed LampArray. Waiting here
# prevents a compiler from attempting to replace a loaded output DLL.
try {
    $hostMutex = [System.Threading.Mutex]::OpenExisting($hostMutexName)
    try {
        if (-not $hostMutex.WaitOne([TimeSpan]::FromSeconds(5))) {
            throw 'Timed out waiting for the lighting background host to release the build output.'
        }
        $hostMutex.ReleaseMutex()
    }
    catch [System.Threading.AbandonedMutexException] {
        # A terminated host no longer owns the output files.
    }
    finally {
        $hostMutex.Dispose()
    }
}
catch [System.Threading.WaitHandleCannotBeOpenedException] {
    # It stopped between the event signal and mutex lookup.
}

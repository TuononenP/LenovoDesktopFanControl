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

Write-Output 'The background-lighting identity and development certificate were removed.'

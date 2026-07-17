[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExternalLocation,

    [string]$PackageVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedExternalLocation = (Resolve-Path -LiteralPath $ExternalLocation).Path
$windowsIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$windowsPrincipal = [Security.Principal.WindowsPrincipal]::new($windowsIdentity)
$isAdministrator = $windowsPrincipal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator) {
    $elevatedArguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', "`"$PSCommandPath`"",
        '-ExternalLocation', "`"$resolvedExternalLocation`""
    )
    if (-not [string]::IsNullOrWhiteSpace($PackageVersion)) {
        $elevatedArguments += @('-PackageVersion', "`"$PackageVersion`"")
    }

    Write-Output 'Administrator access is required to trust the local development certificate.'
    $elevatedProcess = Start-Process `
        -FilePath 'powershell.exe' `
        -Verb RunAs `
        -ArgumentList $elevatedArguments `
        -Wait `
        -PassThru
    if ($elevatedProcess.ExitCode -ne 0) {
        throw "Elevated registration failed with exit code $($elevatedProcess.ExitCode)."
    }
    return
}

$packageName = 'PetriTuononen.LenovoDesktopFanControl'
$publisher = 'CN=LenovoDesktopFanControl'
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestSource = Join-Path $repositoryRoot 'Packaging\AppxManifest.xml'
$externalPath = $resolvedExternalLocation
$executablePath = Join-Path $externalPath 'LenovoDesktopFanControl.exe'
$extensionMetadataPath = Join-Path $externalPath 'public\extension.json'

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "LenovoDesktopFanControl.exe was not found in '$externalPath'. Publish or build the app first."
}
if (-not (Test-Path -LiteralPath $extensionMetadataPath -PathType Leaf)) {
    throw "Background-lighting extension metadata was not found in '$externalPath\public'. Close the app and rebuild it before registering this directory."
}

if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $now = Get-Date
    $PackageVersion = "1.$($now.Year).$($now.Month * 100 + $now.Day).$($now.Hour * 100 + $now.Minute)"
}

if ($PackageVersion -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$') {
    throw "PackageVersion must contain four numeric components, for example 1.2026.717.2130."
}

$versionParts = $PackageVersion.Split('.') | ForEach-Object { [int]$_ }
if ($versionParts | Where-Object { $_ -gt 65535 }) {
    throw 'Every PackageVersion component must be between 0 and 65535.'
}

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$makeAppx = Get-ChildItem -LiteralPath $sdkRoot -Recurse -Filter 'makeappx.exe' |
    Where-Object { $_.Directory.Name -eq 'x64' } |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
    Select-Object -First 1
$signTool = Get-ChildItem -LiteralPath $sdkRoot -Recurse -Filter 'signtool.exe' |
    Where-Object { $_.Directory.Name -eq 'x64' } |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
    Select-Object -First 1

if ($null -eq $makeAppx -or $null -eq $signTool) {
    throw 'The Windows SDK packaging tools were not found. Install the Windows 10/11 SDK and try again.'
}

$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.Subject -eq $publisher -and
        $_.HasPrivateKey -and
        $_.EnhancedKeyUsageList.ObjectId -contains $codeSigningOid -and
        $_.NotAfter -gt (Get-Date).AddDays(30)
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($null -eq $certificate) {
    $certificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $publisher `
        -FriendlyName 'Lenovo Desktop Fan Control development package' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -TextExtension @(
            "2.5.29.37={text}$codeSigningOid",
            '2.5.29.19={text}'
        ) `
        -NotAfter (Get-Date).AddYears(2)
}

$trustedUserCertificate = Get-ChildItem Cert:\CurrentUser\TrustedPeople |
    Where-Object { $_.Thumbprint -eq $certificate.Thumbprint } |
    Select-Object -First 1
$trustedMachineCertificate = Get-ChildItem Cert:\LocalMachine\TrustedPeople |
    Where-Object { $_.Thumbprint -eq $certificate.Thumbprint } |
    Select-Object -First 1

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("LenovoDesktopFanControl-" + [guid]::NewGuid())
$manifestDirectory = Join-Path $temporaryRoot 'manifest'
$packagePath = Join-Path $temporaryRoot 'LenovoDesktopFanControl.Identity.msix'
$certificatePath = Join-Path $temporaryRoot 'LenovoDesktopFanControl.cer'

try {
    New-Item -ItemType Directory -Path $manifestDirectory | Out-Null

    [xml]$manifest = Get-Content -LiteralPath $manifestSource
    $manifest.Package.Identity.Version = $PackageVersion
    $manifest.Save((Join-Path $manifestDirectory 'AppxManifest.xml'))

    & $makeAppx.FullName pack /o /d $manifestDirectory /nv /p $packagePath
    if ($LASTEXITCODE -ne 0) {
        throw "MakeAppx failed with exit code $LASTEXITCODE."
    }

    & $signTool.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $packagePath
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed with exit code $LASTEXITCODE."
    }

    Export-Certificate -Cert $certificate -FilePath $certificatePath | Out-Null

    if ($null -eq $trustedUserCertificate) {
        Import-Certificate `
            -FilePath $certificatePath `
            -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
    }
    if ($null -eq $trustedMachineCertificate) {
        Import-Certificate `
            -FilePath $certificatePath `
            -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    }

    $trustedThumbprints = @(
        Get-ChildItem Cert:\CurrentUser\TrustedPeople, Cert:\LocalMachine\TrustedPeople |
            Where-Object { $_.Thumbprint -eq $certificate.Thumbprint } |
            Select-Object -ExpandProperty Thumbprint
    )
    if ($trustedThumbprints.Count -lt 2) {
        throw 'The development certificate could not be added to both trusted certificate stores.'
    }

    Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue |
        Remove-AppxPackage -ErrorAction Stop

    Add-AppxPackage -Path $packagePath -ExternalLocation $externalPath
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Output "Background-lighting identity registered for '$externalPath'."
Write-Output 'Close and reopen Windows Settings, restart the app, then prioritize it under Personalization > Dynamic Lighting > Background light control.'

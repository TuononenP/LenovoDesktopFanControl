[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CertificatePath,

    [Parameter(Mandatory)]
    [string]$CertificatePassword,

    [Parameter(Mandatory)]
    [string[]]$FilePaths
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
    throw "Release signing certificate was not found: $CertificatePath"
}

$files = @($FilePaths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
if ($files.Count -ne $FilePaths.Count) {
    throw 'One or more release artifacts to sign were not found.'
}

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$signTool = Get-ChildItem -LiteralPath $sdkRoot -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq 'x64' } |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
    Select-Object -First 1

if ($null -eq $signTool) {
    throw 'SignTool was not found in the Windows SDK.'
}

foreach ($file in $files) {
    & $signTool.FullName sign /q /fd SHA256 /f $CertificatePath /p $CertificatePassword `
        /tr 'http://timestamp.digicert.com' /td SHA256 $file
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed for '$file' with exit code $LASTEXITCODE."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "The signature for '$file' is not valid: $($signature.StatusMessage)"
    }
}

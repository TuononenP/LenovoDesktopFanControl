[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TargetPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$publisher = 'CN=LenovoDesktopFanControl'
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
if ($null -eq (Get-PSDrive -Name Cert -ErrorAction SilentlyContinue)) {
    Write-Verbose 'Development signing skipped because the certificate provider is not available.'
    return
}

$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.Subject -eq $publisher -and
        $_.HasPrivateKey -and
        $_.EnhancedKeyUsageList.ObjectId -contains $codeSigningOid -and
        $_.NotAfter -gt (Get-Date)
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($null -eq $certificate) {
    Write-Verbose 'Development signing skipped because the local package certificate is not installed.'
    return
}

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$signTool = Get-ChildItem -LiteralPath $sdkRoot -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq 'x64' } |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
    Select-Object -First 1

if ($null -eq $signTool) {
    Write-Verbose 'Development signing skipped because SignTool was not found.'
    return
}

$outputDirectory = Split-Path -Parent $TargetPath
$assemblyName = [IO.Path]::GetFileNameWithoutExtension($TargetPath)
$files = @(@(
    $TargetPath
    (Join-Path $outputDirectory "$assemblyName.exe")
) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })

if ($files.Count -eq 0) {
    return
}

& $signTool.FullName sign /q /fd SHA256 /sha1 $certificate.Thumbprint /s My $files
if ($LASTEXITCODE -ne 0) {
    throw "SignTool failed with exit code $LASTEXITCODE."
}

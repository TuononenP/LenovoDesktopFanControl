param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

function Get-StableHex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes("LenovoDesktopFanControl.Installer|$Value")
        return ([BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-StableGuid {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $hex = Get-StableHex $Value
    # Mark the deterministic value as an RFC 4122 version-5, variant-1 UUID.
    $hex = $hex.Substring(0, 12) + "5" + $hex.Substring(13)
    $variantNibble = ([Convert]::ToInt32($hex.Substring(16, 1), 16) -band 3) -bor 8
    $hex = $hex.Substring(0, 16) + $variantNibble.ToString("x") + $hex.Substring(17)
    return "$($hex.Substring(0, 8))-$($hex.Substring(8, 4))-$($hex.Substring(12, 4))-$($hex.Substring(16, 4))-$($hex.Substring(20, 12))"
}

function Get-StableId {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prefix,

        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    return "${Prefix}_$((Get-StableHex $Value).Substring(0, 24))"
}

$publishRoot = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
    throw "Publish directory does not exist: $publishRoot"
}

$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFullPath
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$files = @(Get-ChildItem -LiteralPath $publishRoot -File -Recurse |
    Sort-Object FullName)
if ($files.Count -eq 0) {
    throw "Publish directory contains no files: $publishRoot"
}

$filesByDirectory = @{}
$directoryPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$directoryPaths.Add("") | Out-Null

foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($publishRoot.Length).TrimStart(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $relativeDirectory = Split-Path -Parent $relativePath
    if ($null -eq $relativeDirectory) {
        $relativeDirectory = ""
    }
    $relativeDirectory = $relativeDirectory.Replace(
        [IO.Path]::AltDirectorySeparatorChar,
        [IO.Path]::DirectorySeparatorChar)

    if (-not $filesByDirectory.ContainsKey($relativeDirectory)) {
        $filesByDirectory[$relativeDirectory] = [Collections.Generic.List[object]]::new()
    }
    $filesByDirectory[$relativeDirectory].Add([pscustomobject]@{
        FullPath = $file.FullName
        Name = $file.Name
        RelativePath = $relativePath.Replace(
            [IO.Path]::AltDirectorySeparatorChar,
            [IO.Path]::DirectorySeparatorChar)
    })

    $currentDirectory = $relativeDirectory
    while (-not [string]::IsNullOrEmpty($currentDirectory)) {
        $directoryPaths.Add($currentDirectory) | Out-Null
        $currentDirectory = Split-Path -Parent $currentDirectory
        if ($null -eq $currentDirectory) {
            $currentDirectory = ""
        }
    }
}

$directoryIds = @{}
foreach ($directoryPath in $directoryPaths) {
    $directoryIds[$directoryPath] = if ([string]::IsNullOrEmpty($directoryPath)) {
        "INSTALLFOLDER"
    }
    else {
        Get-StableId "dir" $directoryPath
    }
}

$childrenByDirectory = @{}
foreach ($directoryPath in $directoryPaths) {
    if ([string]::IsNullOrEmpty($directoryPath)) {
        continue
    }

    $parent = Split-Path -Parent $directoryPath
    if ($null -eq $parent) {
        $parent = ""
    }
    if (-not $childrenByDirectory.ContainsKey($parent)) {
        $childrenByDirectory[$parent] = [Collections.Generic.List[string]]::new()
    }
    $childrenByDirectory[$parent].Add($directoryPath)
}

$settings = [Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [Text.UTF8Encoding]::new($false)
$writer = [Xml.XmlWriter]::Create($outputFullPath, $settings)

function Write-DirectoryChildren {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$ParentPath
    )

    if (-not $childrenByDirectory.ContainsKey($ParentPath)) {
        return
    }

    foreach ($childPath in @($childrenByDirectory[$ParentPath] | Sort-Object)) {
        $writer.WriteStartElement("Directory")
        $writer.WriteAttributeString("Id", $directoryIds[$childPath])
        $writer.WriteAttributeString("Name", (Split-Path -Leaf $childPath))
        Write-DirectoryChildren $childPath
        $writer.WriteEndElement()
    }
}

function Write-FileComponent {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$RelativeDirectory
    )

    $componentKey = if ([string]::IsNullOrEmpty($RelativeDirectory)) {
        "root"
    }
    else {
        $RelativeDirectory
    }
    $componentId = Get-StableId "cmp" "files|$componentKey"

    $writer.WriteStartElement("Component")
    $writer.WriteAttributeString("Id", $componentId)
    $writer.WriteAttributeString("Directory", $directoryIds[$RelativeDirectory])
    $writer.WriteAttributeString("Guid", (Get-StableGuid "files|$componentKey"))

    foreach ($file in @($filesByDirectory[$RelativeDirectory] | Sort-Object RelativePath)) {
        $writer.WriteStartElement("File")
        $writer.WriteAttributeString("Id", (Get-StableId "fil" $file.RelativePath))
        $writer.WriteAttributeString("Source", $file.FullPath)
        $writer.WriteAttributeString("Name", $file.Name)
        $writer.WriteEndElement()
    }

    if ([string]::IsNullOrEmpty($RelativeDirectory)) {
        foreach ($directoryPath in @($directoryPaths |
                Sort-Object { ($_ -split "[\\/]").Count } -Descending)) {
            $removeKey = if ([string]::IsNullOrEmpty($directoryPath)) {
                "root"
            }
            else {
                $directoryPath
            }
            $writer.WriteStartElement("RemoveFolder")
            $writer.WriteAttributeString("Id", (Get-StableId "rmf" $removeKey))
            $writer.WriteAttributeString("Directory", $directoryIds[$directoryPath])
            $writer.WriteAttributeString("On", "uninstall")
            $writer.WriteEndElement()
        }
    }

    $writer.WriteStartElement("RegistryValue")
    $writer.WriteAttributeString("Root", "HKCU")
    $writer.WriteAttributeString(
        "Key",
        "Software\LenovoDesktopFanControl\InstallerComponents")
    $writer.WriteAttributeString("Name", $componentId)
    $writer.WriteAttributeString("Type", "integer")
    $writer.WriteAttributeString("Value", "1")
    $writer.WriteAttributeString("KeyPath", "yes")
    $writer.WriteEndElement()
    $writer.WriteEndElement()
}

try {
    $writer.WriteStartDocument()
    $writer.WriteStartElement("Wix", "http://wixtoolset.org/schemas/v4/wxs")
    $writer.WriteStartElement("Fragment")
    $writer.WriteStartElement("DirectoryRef")
    $writer.WriteAttributeString("Id", "INSTALLFOLDER")
    Write-DirectoryChildren ""
    $writer.WriteEndElement()

    $writer.WriteStartElement("ComponentGroup")
    $writer.WriteAttributeString("Id", "PublishedApplicationFiles")
    foreach ($relativeDirectory in @($filesByDirectory.Keys | Sort-Object)) {
        Write-FileComponent $relativeDirectory
    }
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndDocument()
}
finally {
    $writer.Dispose()
}

Write-Output "Generated installer authoring for $($files.Count) files in $($filesByDirectory.Count) components."

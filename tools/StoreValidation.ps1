$ErrorActionPreference = 'Stop'
function Read-PackageXml($path, $entryName) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path).Path)
    try {
        $entry = @($zip.Entries | Where-Object { $_.FullName.Replace('\','/') -eq $entryName })
        if ($entry.Count -ne 1) { throw "Package must contain exactly one $entryName." }
        $reader = New-Object System.IO.StreamReader($entry[0].Open())
        try {
            $settings = New-Object System.Xml.XmlReaderSettings
            $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver = $null
            $xmlReader = [System.Xml.XmlReader]::Create($reader, $settings)
            try { $xml = New-Object System.Xml.XmlDocument; $xml.XmlResolver = $null; $xml.Load($xmlReader); return $xml }
            finally { $xmlReader.Dispose() }
        } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
}
function Assert-StorePackage($path, $architecture, $version, $name, $publisher) {
    $manifest = Read-PackageXml $path 'AppxManifest.xml'
    $identity = $manifest.Package.Identity
    if ($identity.Name -ne $name -or $identity.Publisher -ne $publisher -or
        $identity.Version -ne $version -or $identity.ProcessorArchitecture -ne $architecture) {
        throw 'MSIX identity, publisher, version or architecture does not match the configured Store product.'
    }
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path).Path)
    try {
        if (@($zip.Entries | Where-Object { $_.FullName -match '\.pdb$' }).Count -gt 0) { throw 'Store packages must not include debug symbols.' }
        $nativePrefix = "native/win-$architecture/"
        $native = @($zip.Entries | Where-Object { $_.FullName.Replace('\','/').StartsWith($nativePrefix) })
        if (-not ($native | Where-Object { $_.Name -eq 'srt.dll' })) { throw 'Matching native SRT library is missing.' }
        $other = if ($architecture -eq 'arm64') { 'x64' } else { 'arm64' }
        if (@($zip.Entries | Where-Object { $_.FullName.Replace('\','/').StartsWith("native/win-$other/") }).Count -gt 0) {
            throw 'MSIX contains another architecture native payload.'
        }
    } finally { $zip.Dispose() }
}
function Assert-StoreBundle($path, $version, $name, $publisher) {
    $manifest = Read-PackageXml $path 'AppxMetadata/AppxBundleManifest.xml'
    if ($manifest.Bundle.Identity.Name -ne $name -or $manifest.Bundle.Identity.Publisher -ne $publisher -or
        $manifest.Bundle.Identity.Version -ne $version) { throw 'Store bundle identity does not match configured product/version.' }
    $packages = @($manifest.Bundle.Packages.Package | Where-Object { $_.Type -eq 'application' })
    if ($packages.Count -ne 2 -or (@($packages.Architecture | Sort-Object) -join ',') -ne 'arm64,x64') {
        throw 'Store bundle must contain exactly ARM64 and x64 application packages.'
    }
}

param(
    [ValidateSet('arm64', 'x64')][string]$Architecture = 'arm64',
    [string]$OutputDirectory,
    [string]$ArchiveName
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = "artifacts\win-$Architecture" }
if ([string]::IsNullOrWhiteSpace($ArchiveName)) { $ArchiveName = "ScrcpySend-Windows-$Architecture-dev.zip" }
$rid = "win-$Architecture"
$platform = if ($Architecture -eq 'arm64') { 'ARM64' } else { 'x64' }
function Assert-NativeArchitecture($path) {
    $stream = [System.IO.File]::OpenRead($path)
    $reader = New-Object System.IO.BinaryReader($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Not a PE binary: $path" }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or $peOffset -gt $stream.Length - 6) { throw "Invalid PE offset: $path" }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x4550) { throw "Invalid PE signature: $path" }
        $machine = $reader.ReadUInt16()
        $expected = if ($Architecture -eq 'arm64') { 0xAA64 } else { 0x8664 }
        if ($machine -ne $expected) { throw "Wrong architecture for $Architecture package: $path (machine $machine)" }
    } finally { $reader.Dispose(); $stream.Dispose() }
}
foreach ($dll in @('srt.dll', 'avcodec-61.dll', 'avformat-61.dll', 'avutil-59.dll')) {
    $path = Join-Path $root "native\$rid\$dll"
    if (-not (Test-Path $path)) {
        throw 'SRT native dependencies are missing. Run tools\PrepareSrt.ps1 before publishing.'
    }
    Assert-NativeArchitecture $path
}
$project = Join-Path $root 'src\ScrcpySend.App\ScrcpySend.App.csproj'
$output = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $root $OutputDirectory }
if ((Test-Path $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count -gt 0) {
    throw "Publish output must be empty to prevent stale or mixed-architecture files: $output. Choose a new output directory."
}

dotnet publish $project -c Release -r $rid "-p:Platform=$platform" --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw "$Architecture publish failed." }
if (-not (Test-Path (Join-Path $output 'ScrcpySend.App.pri'))) { throw 'Application resource index is missing.' }
Assert-NativeArchitecture (Join-Path $output 'ScrcpySend.App.exe')
foreach ($dll in @(Get-ChildItem (Join-Path $output "native\$rid") -File -Filter '*.dll')) {
    Assert-NativeArchitecture $dll.FullName
}

$assets = Get-Content (Join-Path $root 'src\ScrcpySend.App\obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
$packagePaths = @($assets.libraries.PSObject.Properties |
    Where-Object { $_.Value.type -eq 'package' } |
    ForEach-Object { $_.Value.path.Replace('/', '\') })
foreach ($framework in $assets.project.frameworks.PSObject.Properties.Value) {
    foreach ($dependency in $framework.downloadDependencies) {
        if ($dependency.name -eq 'Microsoft.Windows.SDK.NET.Ref') {
            if ($dependency.version -notmatch '^\[([^,\]]+),\s*\1\]$') {
                throw 'Expected an exact Windows SDK projection package version.'
            }
            $packagePaths += "$($dependency.name.ToLowerInvariant())\$($Matches[1])"
        }
    }
}
$runtime = Get-Content (Join-Path $output 'ScrcpySend.App.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    if ($framework.name -eq 'Microsoft.NETCore.App') {
        $packagePaths += "microsoft.netcore.app.runtime.$rid\$($framework.version)"
    }
}

foreach ($packagePath in ($packagePaths | Sort-Object -Unique)) {
    $package = $null
    foreach ($packageRoot in $packageRoots) {
        $candidate = Join-Path $packageRoot $packagePath
        if (Test-Path $candidate) { $package = $candidate; break }
    }
    if ($null -eq $package) { throw "Restored dependency not found: $packagePath" }
    $notices = @(Get-ChildItem $package -File |
        Where-Object { $_.Extension -eq '.nuspec' -or
            $_.Name -match '^(LICENSE|LICENCE|NOTICE|NOTICES|THIRD-PARTY-NOTICES|COPYRIGHT)(\..*)?$' })
    if ($notices.Count -gt 0) {
        $destination = Join-Path (Join-Path $output 'licenses') $packagePath
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        foreach ($notice in $notices) { Copy-Item $notice.FullName $destination -Force }
    }
}
foreach ($name in @('README.md', 'THIRD_PARTY.md', 'THIRD_PARTY_NOTICES.txt')) {
    Copy-Item (Join-Path $root $name) $output -Force
}
$archive = Join-Path (Join-Path $root 'artifacts') $ArchiveName
New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -CompressionLevel Optimal -Force
Write-Output "Portable development build: $archive"

param(
    [Parameter(Mandatory = $true)][ValidateSet('arm64', 'x64')][string]$Architecture,
    [Parameter(Mandatory = $true)][string]$PublishedDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$PackageName = $env:STORE_PACKAGE_NAME,
    [string]$Publisher = $env:STORE_PUBLISHER,
    [string]$PublisherDisplayName = $env:STORE_PUBLISHER_DISPLAY_NAME
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw 'Store version must be MAJOR.MINOR.PATCH.0.' }
$parsedVersion = [Version]::Parse($Version)
if (@($parsedVersion.Major,$parsedVersion.Minor,$parsedVersion.Build) | Where-Object { $_ -gt 65535 }) {
    throw 'Each package version component must be at most 65535.'
}
if ([string]::IsNullOrWhiteSpace($PackageName) -or $PackageName -notmatch '^[A-Za-z0-9.-]{3,50}$' -or
    [string]::IsNullOrWhiteSpace($Publisher) -or -not $Publisher.StartsWith('CN=') -or
    [string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
    throw 'Configure exact Partner Center STORE_PACKAGE_NAME, STORE_PUBLISHER and STORE_PUBLISHER_DISPLAY_NAME values before packaging.'
}
$published = (Resolve-Path -LiteralPath $PublishedDirectory).Path
foreach ($name in @('ScrcpySend.App.exe','ScrcpySend.App.pri',"native\win-$Architecture\srt.dll")) {
    if (-not (Test-Path (Join-Path $published $name))) { throw "Missing published application file: $name" }
}
if ((Test-Path $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -gt 0) {
    throw 'Store package output must be a new or empty directory.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$staging = Join-Path $output 'staging'
New-Item -ItemType Directory -Path $staging | Out-Null
foreach ($file in @(Get-ChildItem -LiteralPath $published -Recurse -File)) {
    if ($file.Extension -eq '.pdb') { continue }
    $relative = $file.FullName.Substring($published.Length).TrimStart('\')
    $destination = Join-Path $staging $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}

Add-Type -AssemblyName System.Drawing
$assets = Join-Path $staging 'Assets'
New-Item -ItemType Directory -Path $assets | Out-Null
foreach ($size in @(44,50,150)) {
    $bitmap = New-Object System.Drawing.Bitmap($size,$size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [single]($size / 18))
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(22,93,173))
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.DrawRectangle($pen, [single]($size * 0.18), [single]($size * 0.22), [single]($size * 0.64), [single]($size * 0.44))
        $graphics.DrawLine($pen, [single]($size * 0.5), [single]($size * 0.66), [single]($size * 0.5), [single]($size * 0.8))
        $graphics.DrawLine($pen, [single]($size * 0.35), [single]($size * 0.8), [single]($size * 0.65), [single]($size * 0.8))
        $bitmap.Save((Join-Path $assets "Logo$size.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
function Escape-Xml([string]$value) { return [System.Security.SecurityElement]::Escape($value) }
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap rescap">
  <Identity Name="$(Escape-Xml $PackageName)" Publisher="$(Escape-Xml $Publisher)" Version="$Version" ProcessorArchitecture="$Architecture" />
  <Properties>
    <DisplayName>ScrcpySend</DisplayName>
    <PublisherDisplayName>$(Escape-Xml $PublisherDisplayName)</PublisherDisplayName>
    <Logo>Assets\Logo50.png</Logo>
    <Description>Send Windows screen and audio to ScrcpyRecv on a trusted private LAN.</Description>
  </Properties>
  <Resources><Resource Language="en-us" /></Resources>
  <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.22621.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
  <Applications>
    <Application Id="App" Executable="ScrcpySend.App.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="ScrcpySend" Description="Windows screen and audio sender"
                          BackgroundColor="#165DAD" Square44x44Logo="Assets\Logo44.png" Square150x150Logo="Assets\Logo150.png" />
    </Application>
  </Applications>
  <Capabilities>
    <Capability Name="internetClient" />
    <Capability Name="privateNetworkClientServer" />
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@
[System.IO.File]::WriteAllText((Join-Path $staging 'AppxManifest.xml'), $manifest, (New-Object System.Text.UTF8Encoding($false)))
$tools = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools\10.0.26100.4654\bin\10.0.26100.0'
$hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { 'arm64' } else { 'x64' }
$makeappx = Join-Path $tools "$hostArch\makeappx.exe"
if (-not (Test-Path $makeappx)) { throw 'Pinned Windows SDK MakeAppx is unavailable; restore the app project first.' }
$package = Join-Path $output "ScrcpySend-$Architecture.msix"
& $makeappx pack /d $staging /p $package /h SHA256 *> (Join-Path $output 'makeappx.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $output 'makeappx.log') -Tail 20; throw 'MSIX packing/schema validation failed.' }
. (Join-Path $PSScriptRoot 'StoreValidation.ps1')
Assert-StorePackage $package $Architecture $Version $PackageName $Publisher
Write-Output "Unsigned Store package: $package"

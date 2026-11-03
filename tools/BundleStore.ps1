param(
    [Parameter(Mandatory = $true)][string]$Arm64Package,
    [Parameter(Mandatory = $true)][string]$X64Package,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$Version
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'StoreValidation.ps1')
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw 'Store version must be MAJOR.MINOR.PATCH.0.' }
$identity = (Read-PackageXml $Arm64Package 'AppxManifest.xml').Package.Identity
Assert-StorePackage $Arm64Package 'arm64' $Version $identity.Name $identity.Publisher
Assert-StorePackage $X64Package 'x64' $Version $identity.Name $identity.Publisher
if ((Test-Path $OutputDirectory) -and @(Get-ChildItem $OutputDirectory -Force).Count -gt 0) { throw 'Bundle output must be empty.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = (Resolve-Path $OutputDirectory).Path
$packages = Join-Path $output 'packages'
New-Item -ItemType Directory -Path $packages | Out-Null
Copy-Item -LiteralPath $Arm64Package -Destination (Join-Path $packages 'ScrcpySend-arm64.msix')
Copy-Item -LiteralPath $X64Package -Destination (Join-Path $packages 'ScrcpySend-x64.msix')
$hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { 'arm64' } else { 'x64' }
$makeappx = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools\10.0.26100.4654\bin\10.0.26100.0\$hostArch\makeappx.exe"
if (-not (Test-Path $makeappx)) { throw 'Pinned MakeAppx tool is missing; restore the app project first.' }
$bundle = Join-Path $output 'ScrcpySend.msixbundle'
& $makeappx bundle /d $packages /p $bundle /bv $Version *> (Join-Path $output 'makeappx.log')
if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $output 'makeappx.log') -Tail 20; throw 'MSIX bundle validation failed.' }
Assert-StoreBundle $bundle $Version $identity.Name $identity.Publisher
Get-FileHash $bundle -Algorithm SHA256 | Select-Object Algorithm,Hash | ConvertTo-Json | Set-Content (Join-Path $output 'bundle-sha256.json')
Write-Output "Unsigned Store bundle: $bundle"

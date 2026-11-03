param(
    [Parameter(Mandatory = $true)][ValidateSet('arm64','x64')][string]$Architecture,
    [Parameter(Mandatory = $true)][string]$PublishedDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "artifacts\msix-validation-$Architecture"
& (Join-Path $PSScriptRoot 'PackageStore.ps1') -Architecture $Architecture -PublishedDirectory $PublishedDirectory `
    -OutputDirectory $output -Version 1.0.0.0 -PackageName ScrcpySend.StoreValidation `
    -Publisher 'CN=00000000-0000-0000-0000-000000000001' -PublisherDisplayName 'Store validation'
if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging regression failed.' }
. (Join-Path $PSScriptRoot 'StoreValidation.ps1')
$package = Join-Path $output "ScrcpySend-$Architecture.msix"
Assert-StorePackage $package $Architecture '1.0.0.0' 'ScrcpySend.StoreValidation' 'CN=00000000-0000-0000-0000-000000000001'
$rejected = $false
try { Assert-StorePackage $package $Architecture '2.0.0.0' 'ScrcpySend.StoreValidation' 'CN=00000000-0000-0000-0000-000000000001' }
catch { $rejected = $true }
if (-not $rejected) { throw 'A mismatched Store version was incorrectly accepted.' }
Write-Output "MSIX validation passed: $Architecture, reserved test identity, no PDBs, native architecture checked."

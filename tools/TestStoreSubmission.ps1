param([Parameter(Mandatory = $true)][string]$BundlePath)
$ErrorActionPreference = 'Stop'
$names = @('STORE_APP_ID','STORE_PACKAGE_NAME','STORE_PUBLISHER','STORE_TENANT_ID','STORE_CLIENT_ID','STORE_CLIENT_SECRET','GITHUB_ACTIONS')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$env:STORE_APP_ID = 'TESTSTORE123'
$env:STORE_PACKAGE_NAME = 'ScrcpySend.StoreValidation'
$env:STORE_PUBLISHER = 'CN=00000000-0000-0000-0000-000000000001'
$env:STORE_TENANT_ID = '00000000-0000-0000-0000-000000000001'
$env:STORE_CLIENT_ID = '00000000-0000-0000-0000-000000000002'
$env:STORE_CLIENT_SECRET = 'test-only-not-a-credential'
$env:GITHUB_ACTIONS = 'false'
$global:ScrcpyStoreTest = @{ scenario = 'success'; operations = (New-Object System.Collections.Generic.List[string]); update = $null; validBundle = (Resolve-Path $BundlePath).Path }
function Invoke-RestMethod {
    param($Method, $Uri, $ContentType, $Body, $TimeoutSec, $MaximumRedirection, $Headers)
    $global:ScrcpyStoreTest.operations.Add("$Method $Uri")
    if ($Uri -like 'https://login.microsoftonline.com/*') { return @{ access_token = 'test-token' } }
    if ($Method -eq 'Get' -and $Uri -like '*/TESTSTORE123') {
        return @{
            pendingApplicationSubmission = $(if ($global:ScrcpyStoreTest.scenario -eq 'pending') { @{ id = '7' } } else { $null })
            lastPublishedApplicationSubmission = $(if ($global:ScrcpyStoreTest.scenario -eq 'first') { $null } else { @{ id = '6' } })
        }
    }
    if ($Method -eq 'Post' -and $Uri -like '*/submissions') {
        return @{
            id = '8'; fileUploadUrl = 'https://store-test.blob.core.windows.net/upload?sig=test';
            targetPublishMode = 'Immediate'; listings = @{ 'en-us' = @{ description = 'Existing listing' } };
            applicationPackages = @(@{ fileName = 'previous.msixbundle'; fileStatus = 'Uploaded' })
        }
    }
    if ($Method -eq 'Put') { $global:ScrcpyStoreTest.update = $Body | ConvertFrom-Json; return @{} }
    if ($Uri -like '*/commit') { return @{ status = 'CommitStarted' } }
    if ($Uri -like '*/status') { return @{ status = 'Certification' } }
    throw "Unexpected fake Store request: $Method $Uri"
}
function Invoke-WebRequest {
    param($Method, $Uri, $InFile, $Headers, $ContentType, [switch]$UseBasicParsing, $TimeoutSec, $MaximumRedirection)
    if ($Method -ne 'Put' -or $Headers['x-ms-blob-type'] -ne 'BlockBlob') { throw 'Invalid upload request.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($InFile)
    try {
        if ($null -eq $zip.GetEntry('ScrcpySend.msixbundle')) { throw 'Upload ZIP has the wrong package name.' }
    } finally { $zip.Dispose() }
    $global:ScrcpyStoreTest.operations.Add('upload')
    return @{}
}
function Start-Sleep { param($Seconds) }
try {
    & (Join-Path $PSScriptRoot 'SubmitStore.ps1') -BundlePath $BundlePath -Version 1.0.0.0
    if ($global:ScrcpyStoreTest.update.targetPublishMode -ne 'Manual' -or
        $global:ScrcpyStoreTest.update.listings.'en-us'.description -ne 'Existing listing' -or
        $global:ScrcpyStoreTest.update.applicationPackages[0].fileStatus -ne 'PendingDelete' -or
        $global:ScrcpyStoreTest.update.applicationPackages[1].fileStatus -ne 'PendingUpload' -or
        -not $global:ScrcpyStoreTest.operations.Contains('upload')) { throw 'Submission update violated listing/package/publication contract.' }
    foreach ($scenario in @('pending','first')) {
        $global:ScrcpyStoreTest.scenario = $scenario
        $global:ScrcpyStoreTest.operations.Clear()
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'SubmitStore.ps1') -BundlePath $BundlePath -Version 1.0.0.0 }
        catch {
            if ($_.Exception.Message -notmatch 'pending submission|initial submission') { throw }
            $rejected = $true
        }
        if (-not $rejected -or @($global:ScrcpyStoreTest.operations | Where-Object { $_ -like 'Post */submissions' }).Count -gt 0) {
            throw 'Unsafe draft/first-submission state was not rejected before Store mutation.'
        }
    }
    Write-Output 'PASS: mocked Store submission preserves listing, uses correct upload/commit, defaults to manual publication and rejects existing drafts/first submission. No network requests or real credentials used.'
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    Remove-Variable -Name ScrcpyStoreTest -Scope Global
}

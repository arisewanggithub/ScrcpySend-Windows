param(
    [Parameter(Mandatory = $true)][string]$BundlePath,
    [Parameter(Mandatory = $true)][string]$Version,
    [ValidateSet('Manual','Immediate')][string]$PublishMode = 'Manual'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'StoreValidation.ps1')
$required = @('STORE_APP_ID','STORE_PACKAGE_NAME','STORE_PUBLISHER','STORE_TENANT_ID','STORE_CLIENT_ID','STORE_CLIENT_SECRET')
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Missing $name. Configure Store variables and environment secrets; do not put credentials in source." }
}
if ($env:GITHUB_ACTIONS -eq 'true' -and ($env:GITHUB_REF -ne 'refs/heads/main' -or $env:GITHUB_EVENT_NAME -ne 'workflow_dispatch')) {
    throw 'Store submission is allowed only from an approved manual workflow on main.'
}
if ($env:STORE_APP_ID -notmatch '^[A-Za-z0-9]{8,20}$' -or $env:STORE_TENANT_ID -notmatch '^[a-fA-F0-9-]{36}$' -or
    $env:STORE_CLIENT_ID -notmatch '^[a-fA-F0-9-]{36}$') { throw 'Invalid Store application or Entra tenant/client identifiers.' }
Assert-StoreBundle $BundlePath $Version $env:STORE_PACKAGE_NAME $env:STORE_PUBLISHER
$bundle = (Resolve-Path -LiteralPath $BundlePath).Path
$api = 'https://manage.devcenter.microsoft.com/v1.0/my/applications/' + $env:STORE_APP_ID
function Mask([string]$value) {
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Output "::add-mask::$value" }
}
Mask $env:STORE_CLIENT_SECRET
try {
    $authentication = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$($env:STORE_TENANT_ID)/oauth2/token" `
        -ContentType 'application/x-www-form-urlencoded' -Body @{
            grant_type = 'client_credentials'; client_id = $env:STORE_CLIENT_ID; client_secret = $env:STORE_CLIENT_SECRET;
            resource = 'https://manage.devcenter.microsoft.com'
        } -TimeoutSec 60 -MaximumRedirection 0
} catch { throw 'Store authentication failed. Verify the Entra app is associated with Partner Center and has the Manager role.' }
if ([string]::IsNullOrWhiteSpace($authentication.access_token)) { throw 'Store authentication returned no access token.' }
Mask $authentication.access_token
$headers = @{ Authorization = 'Bearer ' + $authentication.access_token }
function Request-Store($method, $uri, $body = $null) {
    $parameters = @{ Method = $method; Uri = $uri; Headers = $headers; TimeoutSec = 120; MaximumRedirection = 0 }
    if ($null -ne $body) { $parameters.ContentType = 'application/json'; $parameters.Body = $body | ConvertTo-Json -Depth 100 -Compress }
    try { return Invoke-RestMethod @parameters }
    catch { throw "Store API $method failed. Inspect the current submission in Partner Center before retrying; existing drafts will not be overwritten." }
}
$application = Request-Store Get $api
if ($null -ne $application.pendingApplicationSubmission) { throw 'The app already has a pending submission. Resolve it in Partner Center; this pipeline never deletes or overwrites drafts.' }
if ($null -eq $application.lastPublishedApplicationSubmission) {
    throw 'Complete the initial submission (including age ratings and listing) in Partner Center before using the MSIX submission API.'
}
$submission = Request-Store Post "$api/submissions"
if ([string]::IsNullOrWhiteSpace($submission.id) -or $submission.id -notmatch '^[0-9]+$') { throw 'Store returned an invalid submission ID.' }
$upload = $null
if (-not [Uri]::TryCreate($submission.fileUploadUrl, [UriKind]::Absolute, [ref]$upload) -or $upload.Scheme -ne 'https' -or
    -not $upload.Host.EndsWith('.blob.core.windows.net', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Store returned an invalid Azure upload URL. A draft may exist; inspect Partner Center.'
}
Mask $upload.AbsoluteUri
$submission.targetPublishMode = $PublishMode
$packages = @()
foreach ($package in @($submission.applicationPackages)) {
    $package.fileStatus = 'PendingDelete'
    $packages += $package
}
$packages += @{ fileName = 'ScrcpySend.msixbundle'; fileStatus = 'PendingUpload'; minimumDirectXVersion = 'None'; minimumSystemRam = 'None' }
$submission.applicationPackages = $packages
Request-Store Put "$api/submissions/$($submission.id)" $submission | Out-Null
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ("scrcpysend-store-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $payload = Join-Path $temporary 'ScrcpySend.msixbundle'
    Copy-Item -LiteralPath $bundle -Destination $payload
    $archive = Join-Path $temporary 'upload.zip'
    Compress-Archive -LiteralPath $payload -DestinationPath $archive
    try {
        Invoke-WebRequest -Method Put -Uri $upload -InFile $archive -Headers @{ 'x-ms-blob-type' = 'BlockBlob' } `
            -ContentType 'application/zip' -UseBasicParsing -TimeoutSec 600 -MaximumRedirection 0 | Out-Null
    } catch { throw 'Store package upload failed. Inspect the pending draft in Partner Center; the pipeline will not delete it or retry a submission blindly.' }
} finally { Remove-Item -LiteralPath $temporary -Recurse -Force }
$commit = Request-Store Post "$api/submissions/$($submission.id)/commit"
if ($commit.status -ne 'CommitStarted') { throw "Store commit did not start (status: $($commit.status))." }
$deadline = [DateTimeOffset]::UtcNow.AddMinutes(10)
do {
    Start-Sleep -Seconds 15
    $status = Request-Store Get "$api/submissions/$($submission.id)/status"
    if ($status.status -match 'Failed|Canceled') { throw "Store submission $($submission.id) failed: $($status.status). Check Partner Center certification reports." }
    if ($status.status -in @('PreProcessing','Certification','PendingPublication','Publishing','Published','Release')) {
        Write-Output "Store accepted submission $($submission.id): $($status.status). Publish mode: $PublishMode. Certification/public availability are separate from CI success."
        return
    }
} while ([DateTimeOffset]::UtcNow -lt $deadline)
throw "Store commit status did not advance within ten minutes. Submission $($submission.id) remains in Partner Center; inspect it before retrying."

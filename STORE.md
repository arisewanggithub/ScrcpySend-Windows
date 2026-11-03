# Microsoft Store publishing

This repository produces **self-contained, untrimmed ARM64 and x64 MSIX
packages** and one bundle for the reserved ScrcpySend Store product. The
portable builds remain available separately. No Store secrets are used in
pull-request CI.

## One-time Partner Center setup

1. Reserve the product as **MSIX or PWA app** (ScrcpySend is an MSIX desktop
   application, not a PWA).
2. Copy the exact **Package/Identity/Name**, **Package/Identity/Publisher**,
   publisher display name and Store ID from **Product Identity**. Do not infer
   package identity from the display name or use the test package identity.
3. Configure repository variables:

   | Variable | Value |
   | --- | --- |
   | `STORE_APP_ID` | Store product ID |
   | `STORE_PACKAGE_NAME` | Exact Package/Identity/Name |
   | `STORE_PUBLISHER` | Exact Package/Identity/Publisher (`CN=...`) |
   | `STORE_PUBLISHER_DISPLAY_NAME` | Publisher display name |

   These identity values are package metadata, not credentials. Keep them in
   GitHub variables rather than committing account-specific values to source.
4. Run **Actions > Microsoft Store** on `main`, specify a version such as
   `1.0.0.0`, and leave **submit** unchecked. Download the Store bundle artifact.
   The fourth version component must be zero; the other components are
   0-65535. New submissions need a version greater than the previous package.
5. Complete the **first submission manually in Partner Center**, including
   packages, descriptions, screenshots, privacy URL, age ratings, availability,
   pricing and declarations. The MSIX submission API requires an initial
   completed submission and subsequent submissions clone the last published
   listing. A newly reserved product cannot be fully initialized by this API.
6. Explain `runFullTrust` in certification notes: this is a native WinUI desktop
   screen/audio sender using Windows.Graphics.Capture, Direct3D, Media
   Foundation and WASAPI. It has no remote input, microphone capture or
   background auto-casting. The first display is selected by default, but
   capture starts only after explicit Play and receiver acceptance.
7. Associate an Entra application with Partner Center and assign the **Manager**
   role according to Microsoft's documentation. Add the following as **secrets
   in the `store-publish` GitHub environment**, never as plaintext repository
   files or workflow inputs:

   - `STORE_TENANT_ID`
   - `STORE_CLIENT_ID`
   - `STORE_CLIENT_SECRET`

8. Require an environment reviewer and restrict deployments to `main`. The
   environment approval is separate from PR approval. Protect secrets from
   forks and never change the workflow to `pull_request_target`.

## Publishing updates

1. Merge changes only after **PR build gate** passes.
2. Run **Microsoft Store** manually on that successful `main` commit.
3. Enter the new package version, enable **submit**, and choose:
   - **Manual** (default): submit for ingestion/certification and hold
     publication for a final Partner Center action.
   - **Immediate**: publish automatically **after Microsoft certification**.
4. Both architecture packages are tested, generated and checked for identity,
   version, native-library architecture and absence of `.pdb` debug symbols.
   The bundle is built from packages in that run and recorded with SHA256.
5. Approve the protected `store-publish` job. It verifies the bundle hash,
   authenticates with Partner Center, refuses any existing pending submission,
   preserves the previous listing/pricing, updates package references, uploads
   the ZIP to Microsoft's SAS URL and commits the submission.
6. CI success means Partner Center accepted the update for processing—not that
   Microsoft certified it or the app is already available. Check Partner Center
   certification/status reports. A failed or interrupted submission may leave a
   draft; resolve it manually before retrying. The pipeline never deletes drafts
   or overwrites an in-progress human submission.

No release is triggered by a PR, normal push, or tag. Store identity is required
before the Store workflow can package anything; fake test identity is used only
by PR packaging validation and is never eligible for real submission.

## Local validation

```powershell
.\tools\Publish.ps1 -Architecture arm64 -OutputDirectory artifacts\store-app-arm64
.\tools\TestStorePackaging.ps1 -Architecture arm64 -PublishedDirectory artifacts\store-app-arm64
```

For real Store packages, set the identity environment variables and call
`tools/PackageStore.ps1` for each architecture, then `tools/BundleStore.ps1`.
The packages are unsigned upload packages; Microsoft signs MSIX packages for
Store distribution. Installing locally for packaged-app testing requires a
matching trusted development certificate. CI does not install certificates,
change Windows security policy, or sideload the app onto this machine.

Basic icons are generated consistently by the packaging script. Replace them
with polished approved product artwork before the first public submission.
Review the proposed [privacy statement](PRIVACY.md) and
[third-party distribution notices](THIRD_PARTY.md), including FFmpeg/interop
LGPL terms, before publishing. Store acceptance and licensing compliance are
not guaranteed merely by successful packing.

## Microsoft references

- [MSIX submission API prerequisites](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services)
- [Manage MSIX submissions](https://learn.microsoft.com/en-us/windows/uwp/monetize/manage-app-submissions)
- [Store package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)

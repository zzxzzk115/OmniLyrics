# Signed macOS releases

[English](macos-release.md) | [简体中文](macos-release.zh-CN.md)

OmniLyrics requires macOS 14 or later. Release builds contain the .NET runtime;
users do not need an SDK. The GUI is distributed as `OmniLyrics.app` for Apple
Silicon and Intel. Portable executables and unsigned CI previews remain separate.

## Signing credentials

Use a **Developer ID Application** certificate with its private key in the local
Keychain. Find its identity and team with:

```bash
security find-identity -v -p codesigning
```

The team ID is the ten-character suffix in the certificate's name. The Apple
account used for notarization must belong to that team.

Create an app-specific password in [Apple Account](https://account.apple.com/)
under **Sign-In and Security → App-Specific Passwords**, then save the credentials
locally:

```bash
xcrun notarytool store-credentials "omnilyrics-notary" \
  --apple-id "YOUR_APPLE_ACCOUNT_EMAIL" \
  --team-id "YOUR_TEAM_ID"
```

Enter the app-specific password only at the secure prompt. Do not put it in a
shell command, repository, issue, or chat. `notarytool` validates the credentials
before saving them in Keychain. These scripts do not export signing keys or
upload credentials to GitHub.

If validation returns HTTP 403 with “A required agreement is missing or has
expired”, the team's Account Holder must check the agreement notices and
membership status in [Apple Developer Account](https://developer.apple.com/account/).
After resolving the account issue, run `store-credentials` again. Do not bypass
credential validation or publish an unnotarized package as a signed release.

## Build and sign

Start from the release tag in a clean checkout. Use an output directory outside
the repository. Xcode Command Line Tools, Python 3, and the .NET 10 SDK are needed.

```bash
export MACOS_SIGNING_IDENTITY="Developer ID Application: YOUR_NAME (YOUR_TEAM_ID)"
bash build/macos/prepare-release.sh /absolute/path/to/release-work
```

This builds both architectures; an optional second argument selects `osx-arm64`
or `osx-x64`. Existing output directories are rejected to prevent mixing releases.
The app version is read from the project. macOS's .NET single-file host contains
the runtime; Avalonia, Skia and HarfBuzz native libraries are kept inside the app
instead of being extracted to a cache at startup. Each native library is signed,
then the main executable and bundle are signed with a secure timestamp and
Hardened Runtime. The executable requests only JIT and Apple Events entitlements.

To package an existing publish directory, use:

```bash
bash build/macos/package.sh /path/to/publish /path/to/package-output 0.4.3
```

For formal signing, publish with `-p:IncludeNativeLibrariesForSelfExtract=false`
and set `MACOS_SIGNING_IDENTITY`. Without that environment variable, the script
creates an **ad-hoc preview**, which is not suitable for the Cask.

## Notarize and staple

Submit each signed app separately:

```bash
bash build/macos/notarize.sh submit /absolute/path/to/release-work/osx-arm64/package/OmniLyrics.app omnilyrics-notary
bash build/macos/notarize.sh submit /absolute/path/to/release-work/osx-x64/package/OmniLyrics.app omnilyrics-notary
```

Submission IDs are saved next to the apps. Check each submission with:

```bash
bash build/macos/notarize.sh finish /absolute/path/to/release-work/osx-arm64/package/OmniLyrics.app omnilyrics-notary
bash build/macos/notarize.sh finish /absolute/path/to/release-work/osx-x64/package/OmniLyrics.app omnilyrics-notary
```

An in-progress submission exits with status 2; wait before checking again. An
unsuccessful submission downloads Apple's diagnostic log and stops. After Apple
accepts a submission, the script staples a temporary copy of the app, verifies it
with `stapler`, `codesign`, and Gatekeeper, and recreates the archive from that copy.
The original bundle stays unchanged, including when macOS protects it after a test
launch. The Cask generator extracts and verifies the final archives. Distribute only
the archive created **after stapling**, so its ticket is available offline.

Run the signed app on a Mac before publishing. Verify launch, Apple Music access
with Automation permission, and lyrics. Test Intel on an Intel Mac or with
Rosetta; a Rosetta test does not replace testing on physical Intel hardware.

## Release assets and Homebrew Cask

Generate the two final archives, checksums and Cask only after both submissions
are accepted:

```bash
python3 build/homebrew/make-cask.py /absolute/path/to/release-work /absolute/path/to/release-assets
```

The generator verifies the stapled bundles again, then produces:

- `omnilyrics-gui-osx-arm64-signed.zip`
- `omnilyrics-gui-osx-x64-signed.zip`
- `SHA256SUMS-macos-signed`
- `omnilyrics.rb`

Upload the archives and checksum manifest to the matching `vVERSION` release in
`zzxzzk115/OmniLyrics`. Do not replace previously published assets: adding the
signed variants preserves existing portable downloads and their checksums.
Copy the generated Cask into `Casks/omnilyrics.rb` in
[`zzxzzk115/homebrew-tap`](https://github.com/zzxzzk115/homebrew-tap), validate its
style and audit, then publish it. The tap must reference uploaded assets with
the exact generated hashes.

Once published, users install with:

```bash
brew install --cask zzxzzk115/tap/omnilyrics
```

The Cask selects the correct architecture and installs `OmniLyrics.app`. It does
not require `media-control`; native Apple Music and Spotify use Apple Events.
Keep Homebrew's normal quarantine and Gatekeeper checks enabled during install
testing. `brew uninstall --cask omnilyrics` removes the app; even `--zap` preserves
the shared OmniLyrics configuration directory, which may also be used by the CLI.

## GitHub-hosted signing and notarization

[Sign and publish macOS release](../.github/workflows/macos-release.yaml) runs on
GitHub-hosted macOS runners when a stable GitHub Release is published. You can
also dispatch it from `master` with an already published `vMAJOR.MINOR.PATCH`
release. It rejects tags not merged into `master`, drafts, prereleases, and tags
that differ from the project version. The tagged source must contain the
publication scripts, so automated upload is available from 0.4.3 onward.
Merging branches or pushing a tag alone does not publish signed packages.

The jobs have separate responsibilities:

1. **Build:** compile Apple Silicon and Intel app bundles from the exact tagged
   commit, without signing credentials.
2. **Sign:** download only that build's immutable artifact ID, sign each native
   library and the app, submit to Apple, wait for acceptance, staple, and verify
   the final archives with Gatekeeper. Credentials are limited to this job; the
   application is never launched here.
3. **Publish:** download only the signing job's immutable artifact ID and upload
   both signed ZIPs, `SHA256SUMS-macos-signed`, and `omnilyrics.rb` to the existing
   Release. Only this job has `contents: write`; it receives no Apple credentials.

The publisher verifies both archive hashes, embedded app versions and generated
Cask before uploading. Identical existing assets are skipped; different files
with the same name are rejected before any uploads. Portable ZIPs, Linux native
packages and their separate checksum manifests are unchanged. The generated
Cask still needs to be committed to `zzxzzk115/homebrew-tap`; the repository's
`GITHUB_TOKEN` cannot write to a different repository.

### One-time repository configuration

In **Settings → Environments**, create `macos-release`. Under **Deployment
branches and tags → Selected branches and tags**, add a **branch** rule `master`
for manual runs and a **tag** rule `v*` for Release-triggered runs. A branch-only
rule would block automatic Release signing. Required reviewers are optional;
if enabled, each signing job waits for approval. A solo maintainer who approves
their own runs must leave “Prevent self-review” unchecked.

Set these **Environment Secrets**, not ordinary repository-wide secrets:

| Secret | Value |
| --- | --- |
| `MACOS_CERTIFICATE_P12_BASE64` | Base64 of an encrypted `.p12` containing a Developer ID Application certificate **and its private key** |
| `MACOS_CERTIFICATE_PASSWORD` | Password chosen when exporting the `.p12` |
| `APPLE_ID` | Apple account email belonging to the certificate's team |
| `APPLE_APP_SPECIFIC_PASSWORD` | Dedicated app-specific password for notarization |

Set these **Environment Variables**:

| Variable | Value |
| --- | --- |
| `APPLE_TEAM_ID` | Ten-character developer team ID from the certificate |
| `MACOS_SIGNING_IDENTITY` | Full `Developer ID Application: NAME (TEAM_ID)` identity or its SHA-1 fingerprint |

Export the certificate and private key yourself from **Keychain Access → My
Certificates** as a password-protected `.p12`. Base64 is not encryption. To send
that file directly to the Environment Secret without printing its contents:

```bash
base64 -i /path/to/DeveloperID.p12 | gh secret set MACOS_CERTIFICATE_P12_BASE64 \
  --repo zzxzzk115/OmniLyrics --env macos-release
```

Enter the other Secrets in GitHub's Environment settings or use `gh secret set`
without `--body` for its interactive prompt. Do not paste passwords, the `.p12`,
or private keys into chat, commits, release assets or command arguments. Delete
the exported file securely according to your own backup policy once configured.
A local `omnilyrics-notary` Keychain profile does not transfer to GitHub runners.
The workflow currently uses Apple ID credentials; App Store Connect API-key
authentication is an alternative that would require adapting the import step.

After this workflow is merged into `master`, publish the matching stable Release
to start automatically. To trigger it manually for an already published release:

```bash
gh workflow run macos-release.yaml --repo zzxzzk115/OmniLyrics \
  --ref master -f tag=v0.4.3
```

If environment reviewers are configured, approve the `macos-release` deployment
in Actions. Apple submission timeouts or rejection stop publication. Diagnostic
JSON is saved as a short-lived Actions artifact on failure. No signing failure
falls back to an unsigned release.

If only **Publish** fails, choose **Re-run failed jobs** so it reuses the original
signed artifact (retained for 30 days). Do not rebuild and replace an existing
signed archive: secure timestamps can produce different bytes on each signing.
If signing failed before publication, rerun the failed jobs to create a fresh
temporary keychain and submit again. None of these steps needs your Mac mini.

The certificate and notary profile live in a temporary keychain under
`RUNNER_TEMP`, with owner-only credential files and a random keychain password.
Secrets are passed as environment variables; shell tracing is disabled. Cleanup
runs on exit and in an `always()` workflow step, and GitHub destroys the hosted
runner after the job. Release Actions are pinned to full commit hashes. No
self-hosted runner, long-lived Mac access, or private key export is needed on a
maintainer's machine during subsequent releases.

Environment approval does not make arbitrary workflow code safe: approved steps
can access their Secrets. Never add `pull_request_target`, untrusted PR checkout,
or arbitrary external artifact inputs to the signing job.

See [GitHub's certificate import guide](https://docs.github.com/en/actions/how-tos/deploy/deploy-to-third-party-platforms/sign-xcode-applications),
[Environment protection](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments),
and [security guidance for public repositories](https://docs.github.com/en/actions/reference/security/secure-use#hardening-for-self-hosted-runners).

References: [Apple Developer ID](https://developer.apple.com/developer-id/),
[Avalonia macOS deployment](https://docs.avaloniaui.net/docs/deployment/macos),
[.NET single-file host design](https://github.com/dotnet/designs/blob/main/accepted/2020/single-file/design.md).

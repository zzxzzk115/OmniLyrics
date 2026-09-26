# Signed macOS releases

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
bash build/macos/package.sh /path/to/publish /path/to/package-output 0.4.1
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
accepts a submission, the script staples its ticket to the app, verifies it with
`stapler`, `codesign`, and Gatekeeper, and recreates the archive. Distribute only
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

## GitHub Actions credentials

The current workflow creates ad-hoc previews; it does not import a private key or
notarize releases. To automate formal releases, use a separate release workflow
and a protected `macos-release` GitHub Environment. Restrict the Environment to
release branches or tags and require a maintainer's approval before releasing its
secrets. Regular pull request jobs must not reference this Environment.

The signing job needs an encrypted `.p12` containing the Developer ID Application
certificate **and private key**, encoded as Base64 in an Environment Secret, plus
its password in a separate Secret. Base64 is not encryption. For notarization,
use either Apple account details with a dedicated app-specific password, or an
App Store Connect team API private key with its key ID and issuer ID. A local
Keychain profile is not automatically available on GitHub runners.

Use a GitHub-hosted macOS runner. Import the certificate into a temporary Keychain
under `RUNNER_TEMP`, register a temporary notarytool profile, and run the signing
and notarization scripts above. Create sensitive files with owner-only permissions.
Pass Secrets through step environment variables, never as interpolated shell
source; do not enable shell tracing or upload credential files as artifacts. An
`always()` cleanup step should delete the temporary Keychain and credential files.

Pin release Actions to reviewed commit hashes. Keep the job token read-only until
the publishing job needs `contents: write`. Environment protection does not make
arbitrary workflow code safe: authorized steps can read their Secrets. Never run
untrusted PR code in a job with signing credentials. Review changes to release
workflows and their scripts before approving a release.

See [GitHub's certificate import guide](https://docs.github.com/en/actions/how-tos/deploy/deploy-to-third-party-platforms/sign-xcode-applications)
and [Environment protection](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments).

References: [Apple Developer ID](https://developer.apple.com/developer-id/),
[Avalonia macOS deployment](https://docs.avaloniaui.net/docs/deployment/macos),
[.NET single-file host design](https://github.com/dotnet/designs/blob/main/accepted/2020/single-file/design.md).

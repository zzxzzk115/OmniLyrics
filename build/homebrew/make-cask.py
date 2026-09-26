#!/usr/bin/env python3
"""Create a cask only from the final, stapled distribution archives."""
import argparse
import hashlib
import json
import plistlib
import re
import subprocess
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("release_directory", type=Path)
parser.add_argument("output_directory", type=Path)
args = parser.parse_args()
versions, hashes = set(), {}
args.output_directory.mkdir(parents=True, exist_ok=True)
for arch in ("arm64", "x64"):
    directory = args.release_directory / ("osx-" + arch) / "package"
    app = directory / "OmniLyrics.app"
    result = json.loads((directory / "notarization-result.json").read_text())
    if result["status"] != "Accepted":
        raise SystemExit(f"{arch}: notarization must be accepted")
    subprocess.run(["xcrun", "stapler", "validate", str(app)], check=True)
    subprocess.run(["codesign", "--verify", "--deep", "--strict", str(app)], check=True)
    subprocess.run(["spctl", "--assess", "--type", "execute", str(app)], check=True)
    version = plistlib.loads((app / "Contents/Info.plist").read_bytes())["CFBundleShortVersionString"]
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise SystemExit(f"Unsupported release version: {version}")
    versions.add(version)
    archive = args.output_directory / f"omnilyrics-gui-osx-{arch}-signed.zip"
    if archive.exists():
        raise SystemExit(f"Refusing to overwrite {archive}")
    # Archive the verified bundle again, never trust an archive created before stapling.
    subprocess.run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", str(app), str(archive)], check=True)
    with archive.open("rb") as stream:
        digest = hashlib.sha256()
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
        hashes[arch] = digest.hexdigest()
if len(versions) != 1:
    raise SystemExit("Both architectures must have the same version")
version = versions.pop()
cask = f'''cask "omnilyrics" do
  arch arm: "arm64", intel: "x64"

  version "{version}"
  sha256 arm:   "{hashes['arm64']}",
         intel: "{hashes['x64']}"

  url "https://github.com/zzxzzk115/OmniLyrics/releases/download/v#{{version}}/omnilyrics-gui-osx-#{{arch}}-signed.zip"
  name "OmniLyrics"
  desc "Desktop lyric viewer with karaoke highlighting"
  homepage "https://github.com/zzxzzk115/OmniLyrics"

  depends_on macos: :sonoma

  app "OmniLyrics.app"

  uninstall quit: "io.github.zzxzzk115.OmniLyrics"

  zap trash: [
    "~/Library/Preferences/io.github.zzxzzk115.OmniLyrics.plist",
    "~/Library/Saved Application State/io.github.zzxzzk115.OmniLyrics.savedState",
  ]
end
'''
(args.output_directory / "omnilyrics.rb").write_text(cask)
(args.output_directory / "SHA256SUMS-macos-signed").write_text("".join(
    f"{hashes[arch]}  omnilyrics-gui-osx-{arch}-signed.zip\n" for arch in ("arm64", "x64")))
print(args.output_directory / "omnilyrics.rb")

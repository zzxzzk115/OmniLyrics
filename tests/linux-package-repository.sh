#!/usr/bin/env bash
# Verify the same public, signed repository installation that users perform.
set -euo pipefail
[[ -f /.dockerenv ]] || { echo 'Use a disposable Docker container.' >&2; exit 1; }
format=${1:?Expected deb or rpm}
repository=${2:?Expected Cloudsmith owner/repository}
version=${3:?Expected release version}
[[ $repository =~ ^[a-z0-9][a-z0-9_-]*/[a-z0-9][a-z0-9_-]*$ ]]
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]
case "$format" in
  deb)
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq curl ca-certificates gnupg
    ;;
  rpm) dnf install -y -q curl ca-certificates gnupg2 ;;
  *) exit 1 ;;
esac
curl -fsSL --retry 3 "https://dl.cloudsmith.io/public/$repository/cfg/setup/bash.$format.sh" -o /tmp/setup-omnilyrics.sh
bash /tmp/setup-omnilyrics.sh
case "$format" in
  deb)
    apt-get update -qq
    apt-get install -y "omnilyrics=$version-1" "omnilyrics-cli=$version-1"
    [[ $(dpkg-query -W -f='${Version}' omnilyrics) == "$version-1" ]]
    [[ $(dpkg-query -W -f='${Version}' omnilyrics-cli) == "$version-1" ]]
    ;;
  rpm)
    dnf --refresh install -y "omnilyrics-$version-1" "omnilyrics-cli-$version-1"
    [[ $(rpm -q --qf '%{VERSION}-%{RELEASE}' omnilyrics) == "$version-1" ]]
    [[ $(rpm -q --qf '%{VERSION}-%{RELEASE}' omnilyrics-cli) == "$version-1" ]]
    ;;
esac
export OMNILYRICS_CONFIG_DIR=/tmp/omnilyrics-repository-config
export DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/omnilyrics-repository-bundle
omnilyrics-cli config show
[[ -x /usr/bin/omnilyrics && -x /usr/bin/omnilyrics-cli ]]
echo "PASS: public $format repository installation for $version on $(uname -m)"

#!/usr/bin/env bash
# Builds ./offline-feed: the NexusCore packages plus every third-party package the host needs, so the
# host can be restored with no internet. Run once on a machine that can reach nuget.org, then copy the
# folder (with SHA256SUMS) to the offline network.
#
#   eng/make-offline-feed.sh <folder-with-NexusCore-nupkg-files> [output-folder]
set -euo pipefail

core="${1:?usage: make-offline-feed.sh <folder-with-NexusCore-nupkg-files> [output-folder]}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="${2:-$repo/offline-feed}"
project="$repo/PostBank.Api/PostBank.Api.csproj"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

core="$(cd "$core" && pwd)"
ls "$core"/NexusCore.Application.*.nupkg >/dev/null 2>&1 || { echo "No NexusCore packages in $core" >&2; exit 1; }

cat > "$work/online.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear />
  <add key="core" value="$core" />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources></configuration>
EOF

echo "1/3 Restoring the host (Core packages + nuget.org) into a scratch package cache..."
dotnet restore "$project" --packages "$work/cache" --configfile "$work/online.config"

echo "2/3 Collecting every package the restore needed into $out ..."
rm -rf "$out"; mkdir -p "$out"
find "$work/cache" -name '*.nupkg' -exec cp {} "$out"/ \;
( cd "$out" && sha256sum *.nupkg > SHA256SUMS )

echo "3/3 Verifying: restoring again from the offline feed only..."
cat > "$work/offline.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="offline-feed" value="$out" /></packageSources></configuration>
EOF
dotnet restore "$project" --packages "$work/verify" --configfile "$work/offline.config" --force --no-cache

echo "Done: $(ls "$out"/*.nupkg | wc -l) packages in $out"

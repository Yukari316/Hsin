#!/usr/bin/env bash
set -euo pipefail

runtime="${1:-linux-x64}"
case "$runtime" in
  win-x64|linux-x64) ;;
  *) printf 'Usage: bash scripts/publish.sh [linux-x64|win-x64]\n' >&2; exit 2 ;;
esac
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
if [[ "$runtime" == win-x64 ]]; then extension=zip; else extension=tar.gz; fi
output="$repo_root/artifacts/Hsin-$runtime.$extension"
web_root="$repo_root/server/wwwroot"

if [[ -e "$output" || -L "$output" ]]; then
  printf 'Output already exists: %s. Archive the previous build first. Do not publish into a running installation.\n' "$output" >&2
  exit 1
fi
if [[ -L "$web_root" ]]; then
  printf 'Refusing to replace a linked wwwroot: %s\n' "$web_root" >&2
  exit 1
fi

cd -- "$repo_root"
pnpm --dir web install --frozen-lockfile
pnpm --dir web run build
if [[ -d "$web_root" ]]; then rm -rf -- "$web_root"; fi
mkdir -p -- "$web_root"
cp -R -- web/dist/. "$web_root/"

mkdir -p -- artifacts
staging="$(mktemp -d "$repo_root/artifacts/.publish-$runtime-XXXXXXXX")"
temporary_archive="$(mktemp "$repo_root/artifacts/.package-$runtime-XXXXXXXX.$extension")"
trap 'printf "Publish stopped; inspect partial build at %s and archive at %s\n" "$staging" "$temporary_archive" >&2' ERR
dotnet publish server/Hsin.csproj -c Release -r "$runtime" --self-contained true -o "$staging"
cp -- LICENSE THIRD-PARTY-NOTICES.md README.md config/hsin.example.json "$staging/"
cp -R -- docs "$staging/docs"
mkdir -p -- "$staging/licenses"
cp -- web/src/vendor/LICENSE "$staging/licenses/MediaMTX-LICENSE"
node scripts/collect-licenses.mjs "$staging"
if [[ "$runtime" == win-x64 ]]; then
  ( cd -- "$staging" && zip -q -r "$temporary_archive" . )
else
  chmod u+x -- "$staging/Hsin"
  tar -czf "$temporary_archive" -C "$staging" .
fi
mv -- "$temporary_archive" "$output"
case "$staging" in "$repo_root"/artifacts/.publish-*) rm -rf -- "$staging" ;; *) printf 'Unsafe staging path: %s\n' "$staging" >&2; exit 1 ;; esac
trap - ERR
printf 'Published %s to %s. Target-system runtime and media acceptance remain separate checks.\n' "$runtime" "$output"

#!/usr/bin/env bash
# Align the sibling Geek-SEO checkout with GeekBackend's Docker/Railway pin.
# GeekBackend references APIs that were removed from Geek-SEO main on 2026-07-07;
# production builds clone the commit in ../Geek-SEO.commit instead.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PIN_FILE="$ROOT/Geek-SEO.commit"
SEO_DIR="$(cd "$ROOT/../Geek-SEO" && pwd)"

if [[ ! -f "$PIN_FILE" ]]; then
  echo "Missing $PIN_FILE" >&2
  exit 1
fi

PIN="$(grep -E '^[0-9a-f]{7,40}$' "$PIN_FILE" | head -1 | tr -d '[:space:]')"
if [[ -z "$PIN" ]]; then
  echo "No pin commit in $PIN_FILE" >&2
  exit 1
fi

if [[ ! -d "$SEO_DIR/.git" ]]; then
  echo "Geek-SEO repo not found at $SEO_DIR" >&2
  exit 1
fi

echo "Syncing $SEO_DIR to pin $PIN ..."
cd "$SEO_DIR"

if ! git cat-file -e "${PIN}^{commit}" 2>/dev/null; then
  echo "Fetching pin commit ..."
  git fetch origin "$PIN"
fi

for dir in GeekSeo.Application GeekSeo.Persistence; do
  git rm -rf --cached -q "$dir" 2>/dev/null || true
  rm -rf "$dir"
done

git checkout "$PIN" -- GeekSeo.Application GeekSeo.Persistence
echo "Done. GeekSeo.Application and GeekSeo.Persistence now match $PIN."

#!/usr/bin/env bash
# Wipe all GeekAPI posts under blog/, use-cases/, and tools/ (operator phase-0).
# Requires GEEK_BACKEND_API_KEY. Optional GEEK_API_URL (default production).
set -euo pipefail

API_URL="${GEEK_API_URL:-https://api.geekatyourspot.com}"
API_KEY="${GEEK_BACKEND_API_KEY:?Set GEEK_BACKEND_API_KEY}"

PREFIXES=('blog/' 'use-cases/' 'tools/')

echo "Fetching all posts from ${API_URL} ..."
posts_json="$(curl -sf -H "X-API-Key: ${API_KEY}" "${API_URL}/api/blog/all?lang=en")"

to_delete="$(POSTS_JSON="${posts_json}" python3 << 'PY'
import json, os
posts = json.loads(os.environ["POSTS_JSON"])
prefixes = ("blog/", "use-cases/", "tools/")
for p in posts:
    slug = p.get("slug") or ""
    if any(slug.startswith(pref) for pref in prefixes):
        print(p["postId"])
PY
)"

if [[ -z "${to_delete//[$'\t\r\n ']}" ]]; then
  echo "No matching posts to delete."
  exit 0
fi

mapfile -t ids <<< "${to_delete}"
echo "Deleting ${#ids[@]} posts ..."

deleted=0
for id in "${ids[@]}"; do
  [[ -z "${id}" ]] && continue
  code="$(curl -s -o /dev/null -w '%{http_code}' -X DELETE \
    -H "X-API-Key: ${API_KEY}" \
    "${API_URL}/api/blog/${id}")"
  if [[ "${code}" == "204" ]]; then
    deleted=$((deleted + 1))
  else
    echo "WARN: DELETE post ${id} returned HTTP ${code}" >&2
  fi
done

echo "Deleted ${deleted} posts."

remaining="$(curl -sf "${API_URL}/api/blog/all?lang=en&status=published" | python3 -c 'import sys,json; print(len(json.load(sys.stdin)))')"
echo "Published posts remaining (public catalog): ${remaining}"

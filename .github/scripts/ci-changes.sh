#!/usr/bin/env bash
# Prints code=false when every file changed since <base> is documentation, and
# code=true otherwise. build.yml uses it to skip the Windows build for a change that
# cannot alter the exe. Anything it is unsure about counts as code.
#
# Usage: bash ci-changes.sh <base-sha>
set -euo pipefail

base="${1:-}"

all() {
  echo "ci-changes: $1, running everything" >&2
  echo code=true
  exit 0
}

[ -n "$base" ] && [ "$base" != 0000000000000000000000000000000000000000 ] || all "no base"
git cat-file -e "$base^{commit}" 2>/dev/null || all "base $base not in clone"

changed=$(git diff --name-only "$base" HEAD)
[ -n "$changed" ] || all "empty diff"

while IFS= read -r f; do
  case "$f" in
    # Only what no build step reads. docs/ holds the README's screenshots.
    *.md | docs/* | LICENSE | *.png | *.jpg | *.jpeg | *.gif | *.webp) ;;
    *) all "$f is not docs" ;;
  esac
done <<< "$changed"

echo code=false

#!/usr/bin/env bash
# Builds a release body for a tag out of the README's Version History.
#
# Called by release.yml when a tag is pushed, and by release-notes.yml to refresh an
# already-published release after a changelog correction. Both go through this one
# script so the two cannot drift into producing different notes for the same tag.
#
# Usage: bash release-notes.sh <tag> <output-file>
#   REPO_URL  base repository URL, e.g. https://github.com/owner/repo
set -euo pipefail

TAG="${1:?tag required, e.g. v1.3.0}"
OUT="${2:?output file required}"
REPO_URL="${REPO_URL:?REPO_URL required}"

VERSION="${TAG#v}"
BODY="$(mktemp)"
trap 'rm -f "$BODY"' EXIT

# Everything between this version's "### vX.Y.Z" heading and the next one. The
# trailing guard stops v1.3.0 from matching a v1.3.01 heading.
awk -v ver="$VERSION" '
  $0 ~ "^### v" ver "([^0-9.]|$)" { found = 1; next }
  found && /^### v/ { exit }
  found { print }
' README.md > "$BODY"

# A release that documents nothing is worse than a failed workflow.
if ! grep -q '[^[:space:]]' "$BODY"; then
  echo "::error::No '### v$VERSION' entry found in README.md Version History"
  exit 1
fi

# Anchor-only links point at the README's own headings. On a release page a relative
# anchor resolves against the release URL instead, so they would be dead links.
sed -i "s@](\#@]($REPO_URL/blob/$TAG/README.md\#@g" "$BODY"

{
  sed '/./,$!d' "$BODY"
  echo
  echo "## Download"
  echo
  echo "\`QuickNetSwitcher-$TAG.exe\` below -- a single self-contained executable, no"
  echo "installer. It requires administrator rights, and it is unsigned, so SmartScreen"
  echo "will warn on first run."

  # Only when the checkout carries enough history to find the previous tag.
  PREV="$(git describe --tags --abbrev=0 "$TAG^" 2>/dev/null || true)"
  if [ -n "$PREV" ]; then
    echo
    echo "**Full changelog**: $REPO_URL/compare/$PREV...$TAG"
  fi
} > "$OUT"

cat "$OUT"

#!/usr/bin/env bash
# Fails when src/Oblodai/Generated is not what the backend's generator makes of the gateway's contract.
#
# Regenerates into a temporary directory with the backend's tools/sdkgen (from
# services/core/api/openapi.json, checked against names.lock) and compares file by file, README
# method tables included. The backend
# checkout is $OBLODAI_BACKEND, else ../oblodai-backend next to this repository. Without a backend
# that has tools/sdkgen the check is skipped, loudly; with --require it fails instead.
# Fix drift by regenerating (`make sdk` in the backend), never by hand.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BACKEND="${OBLODAI_BACKEND:-$ROOT/../oblodai-backend}"
SDKGEN="$BACKEND/tools/sdkgen"
SPEC="$BACKEND/services/core/api/openapi.json"

if [ ! -d "$SDKGEN/cmd/sdkgen" ] || [ ! -f "$SPEC" ]; then
  message="no generator at $SDKGEN (set OBLODAI_BACKEND to the backend checkout)"
  if [ "${1:-}" = "--require" ]; then
    echo "check-generated: $message" >&2
    exit 1
  fi
  echo "  (skipped: $message)"
  exit 0
fi

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
# The READMEs carry a generated method table: regenerate copies of them too. -frozen-lock: a new
# method the lock does not name yet is drift here, not something to add.
cp "$ROOT/README.md" "$ROOT/README.ru.md" "$TMP/"
(cd "$SDKGEN" && GOTOOLCHAIN="${GOTOOLCHAIN:-go1.26.6}" go run ./cmd/sdkgen \
  -spec "$SPEC" -lang dotnet -out "$TMP" -lock "$ROOT/names.lock" -frozen-lock)

stale=0
if ! diff -r "$TMP/src/Oblodai/Generated" "$ROOT/src/Oblodai/Generated" >/dev/null; then
  echo "check-generated: src/Oblodai/Generated is stale:" >&2
  diff -rq "$TMP/src/Oblodai/Generated" "$ROOT/src/Oblodai/Generated" >&2 || true
  stale=1
fi
for readme in README.md README.ru.md; do
  if ! cmp -s "$TMP/$readme" "$ROOT/$readme"; then
    echo "check-generated: the method table of $readme is stale" >&2
    stale=1
  fi
done
if [ "$stale" = 1 ]; then
  echo "regenerate with \`make sdk\` in the backend" >&2
  exit 1
fi
echo "generated code and README method tables match $SPEC"

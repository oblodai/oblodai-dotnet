#!/usr/bin/env bash
# Fails when src/Oblodai/Generated is not what the backend's generator makes of the gateway's contract.
#
# Regenerates into a temporary directory with the backend's tools/sdkgen (from
# services/core/api/openapi.json, checked against names.lock) and compares file by file. The backend
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
(cd "$SDKGEN" && GOTOOLCHAIN="${GOTOOLCHAIN:-go1.26.6}" go run ./cmd/sdkgen \
  -spec "$SPEC" -lang dotnet -out "$TMP" -lock "$ROOT/names.lock")

if ! diff -r "$TMP/src/Oblodai/Generated" "$ROOT/src/Oblodai/Generated" >/dev/null; then
  echo "check-generated: src/Oblodai/Generated is stale:" >&2
  diff -rq "$TMP/src/Oblodai/Generated" "$ROOT/src/Oblodai/Generated" >&2 || true
  echo "regenerate with \`make sdk\` in the backend" >&2
  exit 1
fi
echo "generated code matches $SPEC"

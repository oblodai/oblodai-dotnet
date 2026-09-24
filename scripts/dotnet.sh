#!/usr/bin/env bash
# Runs the .NET SDK in docker (no local toolchain needed): ./scripts/dotnet.sh build -c Release
# DOTNET_ENTRYPOINT=bash runs a shell in the same container instead (see scripts/ci.sh).
# Caches (NuGet packages, CLI home) live in .cache/ inside the repository, git-ignored.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
EXTRA=()
if [ -n "${OBLODAI_BACKEND:-}" ]; then
  EXTRA+=(-v "$OBLODAI_BACKEND:/backend:ro" -e OBLODAI_BACKEND=/backend)
fi
exec docker run --rm --label oblodai.sdkcheck=1 --memory "${DOTNET_DOCKER_MEMORY:-3g}" \
  -e NUGET_PACKAGES=/src/.cache/nuget -e DOTNET_CLI_HOME=/src/.cache/home \
  -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
  "${EXTRA[@]}" -v "$ROOT:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 "${DOTNET_ENTRYPOINT:-dotnet}" "$@"

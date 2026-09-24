#!/usr/bin/env bash
# Every gate the CI runs, in the order that fails fastest. Usage: ./scripts/ci.sh [--live]
#
# The .NET toolchain runs in docker (scripts/dotnet.sh, SDK 10 builds net8.0 and net10.0); the drift
# check needs Go on the host for the backend's generator. The backend checkout is $OBLODAI_BACKEND,
# else ../oblodai-backend: the drift check and the conformance suite read it.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ -z "${OBLODAI_BACKEND:-}" ] && [ -d ../oblodai-backend ]; then
  OBLODAI_BACKEND="$(cd ../oblodai-backend && pwd)"
fi
export OBLODAI_BACKEND="${OBLODAI_BACKEND:-}"

echo "== generated code drift"
./scripts/check-generated.sh ${OBLODAI_BACKEND:+--require}

FILTER="FullyQualifiedName!~Oblodai.Tests.Live"
if [ "${1:-}" = "--live" ]; then
  : "${OBLODAI_LIVE_URL:?set OBLODAI_LIVE_URL}"
  FILTER="FullyQualifiedName~Oblodai.Tests.Live"
fi

echo "== build (warnings are errors), format, tests (unit, contract, conformance, examples, README), pack"
rm -rf .cache/pack
DOTNET_ENTRYPOINT=bash ./scripts/dotnet.sh -euo pipefail -c "
  dotnet restore Oblodai.sln -m:1
  dotnet build Oblodai.sln -c Release --no-restore -m:1 -warnaserror
  dotnet format Oblodai.sln --verify-no-changes --no-restore --exclude src/Oblodai/Generated
  dotnet test tests/Oblodai.Tests -c Release --no-build -m:1 --filter '$FILTER'
  dotnet pack src/Oblodai -c Release --no-build -o .cache/pack
"

echo "== package"
python3 - <<'PY'
import pathlib, re, sys, zipfile

packages = [p for p in pathlib.Path(".cache/pack").glob("Oblodai.*.nupkg") if not p.name.endswith(".symbols.nupkg")]
if len(packages) != 1:
    sys.exit(f"expected one Oblodai package, got {packages}")
package = packages[0]
version = re.search(r"<Version>([^<]+)</Version>", pathlib.Path("src/Oblodai/Oblodai.csproj").read_text()).group(1)
if package.name != f"Oblodai.{version}.nupkg":
    sys.exit(f"unexpected package {package.name}")
names = zipfile.ZipFile(package).namelist()
for path in ("lib/net8.0/Oblodai.dll", "lib/net10.0/Oblodai.dll", "lib/net8.0/Oblodai.xml", "README.md", "AGENTS.md", "LICENSE"):
    if path not in names:
        sys.exit(f"{package.name} is missing {path}")
print(f"package: {package.name} carries net8.0 and net10.0, docs, README and AGENTS.md")
PY

echo "all gates green"

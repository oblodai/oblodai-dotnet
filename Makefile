# Local gates; the .NET toolchain runs in docker (scripts/dotnet.sh), the drift check needs Go.
# OBLODAI_BACKEND points at the backend checkout (default ../oblodai-backend).

.PHONY: ci live drift build test

ci:            ## every gate CI runs: drift, build, format, tests, conformance, package
	./scripts/ci.sh

live:          ## the live tier against a running gateway (needs OBLODAI_LIVE_URL)
	./scripts/ci.sh --live

drift:         ## fail when src/Oblodai/Generated is stale
	./scripts/check-generated.sh --require

build:
	./scripts/dotnet.sh build Oblodai.sln -m:1

test:
	./scripts/dotnet.sh test tests/Oblodai.Tests -m:1 --filter 'FullyQualifiedName!~Oblodai.Tests.Live'

#!/usr/bin/env bash
# Load credentials from .env and run the AuthorizeNETtest suite.
#
# Usage:
#   ./scripts/run-tests.sh                 # run integration tests against the sandbox
#   ./scripts/run-tests.sh --mock          # run NMock3 unit tests (currently broken on .NET 10)
#   ./scripts/run-tests.sh --all           # run every test
#   ./scripts/run-tests.sh -- <dotnet args> # pass-through to `dotnet test` after `--`
#
# Any arguments after `--` are forwarded to `dotnet test`.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env.local"

if [[ ! -f "$env_file" ]]; then
    echo "error: $env_file not found. Copy .env.example to .env.local and fill it in." >&2
    exit 1
fi

# Load .env: export every KEY=VALUE line, skip comments and blanks.
set -o allexport
# shellcheck disable=SC1090
source "$env_file"
set +o allexport

if [[ -z "${API_LOGIN_ID:-}" || -z "${TRANSACTION_KEY:-}" ]]; then
    echo "error: API_LOGIN_ID and TRANSACTION_KEY must be set in $env_file" >&2
    exit 1
fi

mode="integration"
passthrough=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --mock)         mode="mock";        shift ;;
        --integration)  mode="integration"; shift ;;
        --all)          mode="all";         shift ;;
        --) shift; passthrough=("$@"); break ;;
        *)  passthrough+=("$1"); shift ;;
    esac
done

case "$mode" in
    integration) filter='FullyQualifiedName~SampleTest|FullyQualifiedName~AuthorizeNet.Api.Controllers.Test' ;;
    mock)        filter='FullyQualifiedName~MockTest' ;;
    all)         filter='' ;;
esac

cd "$repo_root"

args=(test AuthorizeNETtest/AuthorizeNETtest.csproj -c Release)
[[ -n "$filter" ]] && args+=(--filter "$filter")
args+=("${passthrough[@]}")

echo "Running: dotnet ${args[*]}"
exec dotnet "${args[@]}"

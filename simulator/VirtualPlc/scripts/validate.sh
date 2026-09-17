#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PJ1_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT_ROOT="$PJ1_ROOT/VirtualPlc"
TEST_PROJECT="$PROJECT_ROOT/tests/VirtualPlc.SystemValidation/VirtualPlc.SystemValidation.csproj"
COMPAT_PROJECT="$PROJECT_ROOT/tests/VirtualPlc.SystemValidation/VirtualPlc.CompatibilityHost.csproj"
PRODUCTION_PROJECT="$PROJECT_ROOT/src/VirtualPlc/VirtualPlc.csproj"
RESULT_DIR="$PROJECT_ROOT/test-results"

export DOTNET_CLI_HOME="$PROJECT_ROOT/.dotnet-home"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$PROJECT_ROOT/.nuget/packages"
export NUGET_HTTP_CACHE_PATH="$PROJECT_ROOT/.nuget/http-cache"
export TMPDIR="$PROJECT_ROOT/.tmp"

mkdir -p "$RESULT_DIR" "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH" "$TMPDIR"

find_free_port() {
  local start="$1"
  local end="$2"
  local candidate
  for candidate in $(seq "$start" "$end"); do
    if ! (exec 3<>"/dev/tcp/127.0.0.1/$candidate") 2>/dev/null; then
      echo "$candidate"
      return 0
    fi
  done
  return 1
}

HTTP_PORT="$(find_free_port 50800 50899)"
MODBUS_PORT="$(find_free_port 15020 15119)"
SDK_VERSION="$(dotnet --version)"
HOST_PID=""

cleanup() {
  if [[ -n "$HOST_PID" ]] && kill -0 "$HOST_PID" 2>/dev/null; then
    kill "$HOST_PID" 2>/dev/null || true
    wait "$HOST_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

cd "$PJ1_ROOT"

dotnet restore "$TEST_PROJECT" --configfile "$PROJECT_ROOT/NuGet.Config" --nologo
dotnet build "$TEST_PROJECT" --no-restore --configuration Release --nologo

if dotnet --list-sdks | grep -q '^10\.'; then
  dotnet restore "$PRODUCTION_PROJECT" --configfile "$PROJECT_ROOT/NuGet.Config" --nologo
  dotnet build "$PRODUCTION_PROJECT" --no-restore --configuration Release --nologo
  HOST_DLL="$PROJECT_ROOT/src/VirtualPlc/bin/Release/net10.0/VirtualPlc.dll"
  HOST_OUT="$(dirname "$HOST_DLL")"
  EXECUTION_TARGET="net10.0"
  RUN_MODE="production"
  GATE_STATUS="PASS"
  GATE_REASON="Production project restored and built with an installed .NET 10 SDK."
else
  dotnet restore "$COMPAT_PROJECT" --configfile "$PROJECT_ROOT/NuGet.Config" --nologo
  dotnet build "$COMPAT_PROJECT" --no-restore --configuration Release --nologo
  HOST_DLL="$PROJECT_ROOT/tests/VirtualPlc.SystemValidation/bin/Release/net8.0/VirtualPlc.CompatibilityHost.dll"
  HOST_OUT="$(dirname "$HOST_DLL")"
  EXECUTION_TARGET="net8.0"
  RUN_MODE="compatibility-host"
  GATE_STATUS="NOT RUN"
  GATE_REASON="The production project targets net10.0, but no .NET 10 SDK is installed; behavior tests compiled the same source with net8.0."
fi

(
  cd "$HOST_OUT"
  Dashboard__OpenBrowserOnStart=false \
  Modbus__ListenAddress=127.0.0.1 \
  Modbus__Port="$MODBUS_PORT" \
  dotnet "$HOST_DLL" --urls "http://127.0.0.1:$HTTP_PORT"
) >"$RESULT_DIR/host.log" 2>&1 &
HOST_PID=$!

READY=0
for _ in $(seq 1 100); do
  if curl --silent --fail --max-time 1 "http://127.0.0.1:$HTTP_PORT/health" >/dev/null 2>&1; then
    READY=1
    break
  fi
  if ! kill -0 "$HOST_PID" 2>/dev/null; then
    break
  fi
  sleep 0.1
done

if [[ "$READY" -ne 1 ]]; then
  echo "VirtualPlc validation host did not become ready. See $RESULT_DIR/host.log" >&2
  exit 2
fi

set +e
dotnet "$PROJECT_ROOT/tests/VirtualPlc.SystemValidation/bin/Release/net8.0/VirtualPlc.SystemValidation.dll" \
  --http "http://127.0.0.1:$HTTP_PORT" \
  --modbus-host 127.0.0.1 \
  --modbus-port "$MODBUS_PORT" \
  --repo-root "$PJ1_ROOT" \
  --json-output "$RESULT_DIR/latest.json" \
  --markdown-output "$RESULT_DIR/latest.md" \
  --sdk-version "$SDK_VERSION" \
  --execution-target "$EXECUTION_TARGET" \
  --mode "$RUN_MODE" \
  --production-gate-status "$GATE_STATUS" \
  --production-gate-reason "$GATE_REASON"
VALIDATION_EXIT=$?
set -e

cleanup
HOST_PID=""

echo "Report: $RESULT_DIR/latest.md"
echo "Machine result: $RESULT_DIR/latest.json"
echo "Host log: $RESULT_DIR/host.log"
exit "$VALIDATION_EXIT"

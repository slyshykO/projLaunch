#!/usr/bin/env bash
# Linux/Ubuntu counterpart of build.bat: builds the F# build helper and runs it.
#
# Usage: ./build.sh <subcommand> [options]   (see ./build.sh help)
#        ./build.sh self-build               rebuild the build helper only
#        ./build.sh clean                    remove generated artifacts (needs pwsh)

set -u

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$ROOT_DIR/build"
PROJECT_NAME="build"
PROJECT_FRAMEWORK="net10.0"
REBUILT=0

case "$(uname -m)" in
    aarch64 | arm64) PROJECT_RUNTIME="linux-arm64" ;;
    *) PROJECT_RUNTIME="linux-x64" ;;
esac

cd "$ROOT_DIR" || exit 1

# Build-time tools such as the Serde.FS.Json generator target net9.0; let them run on newer runtimes.
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet SDK was not found on PATH. Install the SDK selected by global.json." >&2
    exit 1
fi

# Clean before loading the helper so its own bin/obj can also be removed.
if [ "${1:-}" = "clean" ]; then
    if ! command -v pwsh >/dev/null 2>&1; then
        echo "Cleaning requires PowerShell (pwsh). Install it, e.g. 'sudo snap install powershell --classic'." >&2
        exit 1
    fi
    exec pwsh -NoProfile -File "$ROOT_DIR/scripts/clean.ps1"
fi

# Match the build helper to the SDK selected by global.json.
case "$(dotnet --version)" in
    11.*) PROJECT_FRAMEWORK="net11.0" ;;
esac

BUILD_EXE="$PROJECT_DIR/bin/Release/$PROJECT_FRAMEWORK/$PROJECT_RUNTIME/publish/$PROJECT_NAME"

self_build() {
    REBUILT=1
    # Unlike build.bat this does not use NativeAOT, which would require clang and zlib headers.
    dotnet publish \
        "$PROJECT_DIR/$PROJECT_NAME.fsproj" \
        --configuration Release -f "$PROJECT_FRAMEWORK" --tl:on --sc --runtime "$PROJECT_RUNTIME" \
        /property:TargetFrameworks="$PROJECT_FRAMEWORK" \
        /property:PublishTrimmed=True \
        /property:DebugType=None \
        /property:DebugSymbols=False \
        /property:PublishSingleFile=False \
        /property:InvariantGlobalization=True || return $?
    echo
    ls -la "$PROJECT_DIR/bin/Release/$PROJECT_FRAMEWORK/$PROJECT_RUNTIME/publish"
}

report_error() {
    echo
    echo "$1 failed. Please check the output above for details."
    exit "$2"
}

if [ "${1:-}" = "self-build" ]; then
    self_build || report_error "Self-build" $?
    exit 0
fi

if [ ! -x "$BUILD_EXE" ]; then
    self_build || report_error "Self-build" $?
    if [ ! -x "$BUILD_EXE" ]; then
        echo "Self-build did not produce \"$BUILD_EXE\"."
        report_error "Self-build" 1
    fi
fi

"$BUILD_EXE" "$@"
code=$?

if [ "$code" -eq 42 ]; then
    if [ "$REBUILT" -eq 1 ]; then
        echo "Self-check still fails after rebuilding the build tool."
        report_error "Build" 42
    fi
    echo "Build tool sources changed. Rebuilding..."
    self_build || report_error "Self-build" $?
    "$BUILD_EXE" "$@"
    code=$?
fi

if [ "$code" -ne 0 ]; then
    report_error "Build" "$code"
fi
exit 0

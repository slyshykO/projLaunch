#!/usr/bin/env bash
set -euo pipefail

# Keep both Rust linking and C dependencies on Ubuntu 22.04's glibc ABI.
# cc-rs may append rustc's raw triple, overriding our version-pinned Zig target.
: "${ZIG_BIN:?Set ZIG_BIN to the Linux Zig executable}"
args=()
for arg in "$@"; do
    case "$arg" in
        --target=*) continue ;;
    esac
    args+=("$arg")
done
exec "$ZIG_BIN" cc -target x86_64-linux-gnu.2.35 "${args[@]}"

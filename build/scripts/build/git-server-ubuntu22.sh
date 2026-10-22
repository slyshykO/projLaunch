#!/usr/bin/env bash
set -euo pipefail

case "${1:-}" in
    '') profile=debug ; cargo_profile=() ;;
    --release) profile=release ; cargo_profile=(--release) ;;
    *) echo "Usage: bash $0 [--release]" >&2; exit 2 ;;
esac
if (( $# > 1 )); then
    echo "Usage: bash $0 [--release]" >&2
    exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "$script_dir/../../.." && pwd)"
target=x86_64-unknown-linux-gnu
target_dir="$project_root/crates/target/ubuntu22"
binary_name=project-launch-git-server

# wsl.exe --exec does not load the user's interactive shell profile.
if [[ -f "$HOME/.cargo/env" ]]; then
    source "$HOME/.cargo/env"
fi
for tool in cargo rustup readelf install; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "error: $tool is required inside Linux/WSL for the ubuntu22 build" >&2
        exit 127
    fi
done

find_zig() {
    local candidate
    if [[ -n "${ZIG_BIN:-}" ]]; then
        if [[ -x "$ZIG_BIN" ]] && "$ZIG_BIN" version >/dev/null 2>&1; then
            printf '%s\n' "$ZIG_BIN"
            return
        fi
        echo "error: ZIG_BIN is not a working Linux Zig executable: $ZIG_BIN" >&2
        return 1
    fi
    for candidate in "$(command -v zig || true)" "$HOME/bin/zig" "$HOME"/bin/zig*/zig; do
        if [[ -x "$candidate" ]] && "$candidate" version >/dev/null 2>&1; then
            printf '%s\n' "$candidate"
            return
        fi
    done
    echo "error: Zig is required inside Linux/WSL; put it on PATH, in ~/bin/zig*, or set ZIG_BIN" >&2
    return 127
}
ZIG_BIN="$(find_zig)"
export ZIG_BIN
echo "Building Git server for ubuntu22 ($target, glibc <= 2.35, $profile) with Zig $("$ZIG_BIN" version)"

rustup target add "$target"
mkdir -p "$target_dir/toolchain"
# Install an executable wrapper without changing source permissions on the Windows checkout.
linker="$target_dir/toolchain/zigcc-x86_64-gnu.sh"
install -m 755 "$script_dir/zigcc-x86_64-gnu.sh" "$linker"
export CARGO_TARGET_X86_64_UNKNOWN_LINUX_GNU_LINKER="$linker"
export CC_x86_64_unknown_linux_gnu="$linker"

cd -- "$project_root"
cargo build --manifest-path "$project_root/crates/Cargo.toml" -p git-server --locked \
    --target "$target" --target-dir "$target_dir" "${cargo_profile[@]}"

binary="$target_dir/$target/$profile/$binary_name"
versions="$(LC_ALL=C readelf --version-info "$binary")"
required="$(printf '%s\n' "$versions" | { grep -oE 'GLIBC_[0-9]+(\.[0-9]+)+' || true; } | sort -Vu | tail -n 1)"
if [[ -z "$required" || "$versions" == *GLIBC_PRIVATE* ]]; then
    echo "error: could not verify public glibc requirements of $binary" >&2
    exit 1
fi
highest="$(printf '%s\n' "${required#GLIBC_}" 2.35 | sort -V | tail -n 1)"
if [[ "$highest" != 2.35 ]]; then
    echo "error: $binary requires $required, newer than Ubuntu 22.04's glibc 2.35" >&2
    exit 1
fi

destination="$project_root/src-tauri/binaries"
mkdir -p "$destination"
install -m 755 "$binary" "$destination/$binary_name-$target"
echo "Git server copied to $destination/$binary_name-$target (requires $required)"

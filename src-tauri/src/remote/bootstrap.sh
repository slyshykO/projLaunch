#!/bin/sh
# Shared Linux installer; stdin carries the binary only for install.
set -eu
umask 077
mode=$1
digest=$2
base=$3
[ "$(uname -s)" = Linux ] && [ "$(uname -m)" = x86_64 ] || { echo 'The bundled Git server requires x86_64 Linux' >&2; exit 1; }
if [ -z "$base" ]; then base="$HOME/.local/share/projlaunch/git-server"; fi
case "$base" in /*) ;; *) echo 'Git server directory must be absolute' >&2; exit 1;; esac
directory="$base/$digest"
server="$directory/project-launch-git-server"
valid() {
    [ -f "$server" ] && [ -x "$server" ] && [ "$(sha256sum -- "$server" | cut -d ' ' -f 1)" = "$digest" ]
}
if [ "$mode" = probe ]; then
    valid || exit 10
    info=$("$server" --version-json) || exit 10
else
    mkdir -p -- "$directory"
    temporary=$(mktemp "$directory/.install-XXXXXXXX")
    trap 'rm -f -- "$temporary"' EXIT HUP INT TERM
    cat > "$temporary"
    [ "$(sha256sum -- "$temporary" | cut -d ' ' -f 1)" = "$digest" ] || { echo 'Git server transfer checksum mismatch' >&2; exit 1; }
    chmod 700 -- "$temporary"
    info=$("$temporary" --version-json)
    # Atomic replacement also repairs an interrupted installation; running versions remain usable.
    mv -f -- "$temporary" "$server"
    trap - EXIT HUP INT TERM
fi
printf '%s\n%s\n' "$server" "$info"

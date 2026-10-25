# Remote Repositories

[Documentation index](README.md) · [Project overview](../README.md) · [Development entry point](../AGENTS.md)

Run commands from the repository root. Source paths below are relative to that root.

## Git in WSL

Edit the project, select **Remote / WSL**, enter the installed distribution name
(for example `Ubuntu`) and an absolute path inside it (`/home/user/project` or a
Linux `.code-workspace`/solution path). The Git indicator opens the same history,
branches, working-tree and diff views as a local repository. Tabs display the WSL
distribution and restore it after restart. A repository is identified by both its
endpoint and its path; equal paths in different distributions are separate tabs.
The existing project remote setting supplies the endpoint; no new database table
or manually configured server address is needed.

The distribution must have Git on its normal executable search path and support
the bundled x86_64 glibc Linux binary (Ubuntu 22.04 or compatible). The portable
ZIP includes it. For development, build it once using
`build.bat build-git-server ubuntu22 --release`; debug GUI builds reuse this file.

On first Git use, the launcher checks the installed binary's SHA-256, version and
protocol. If missing or different, it transfers its bundled binary into
`~/.local/share/projlaunch/git-server/<sha256>/project-launch-git-server`, validates
it and installs it atomically. Unchanged binaries are reused; corrupted or
interrupted installations are repaired. Each binary has its own directory, so
updating does not overwrite a server used by another running app version.
The launcher starts the server through `wsl.exe --distribution ... --exec` and
uses stdin/stdout for RPC. No listening port, sudo or Rust installation is needed.
Each endpoint has its own connection; disconnect/app exit closes its server.
Missing distributions, Git or incompatible binaries are reported as Git errors.

## Git over SSH

Select **Remote / SSH** in the project editor and use either:

- **Host / IP** `192.0.2.10` and **username** `alex`.
- **Config alias** `devbox`, leaving username empty to use `User` from OpenSSH
  configuration. An explicit username overrides the configured user.

The project path is an absolute Linux path such as `/home/alex/project`.
The app uses the system `ssh` client and its usual configuration
(`~/.ssh/config`, or `%USERPROFILE%\.ssh\config` on Windows), including ports,
identity files, ssh-agent and ProxyJump. For example:

```sshconfig
Host devbox
    HostName 192.0.2.10
    User alex
    Port 2222
    IdentityFile ~/.ssh/id_ed25519
```

First confirm that `ssh devbox true` works with key/agent authentication and the
host's trusted key in `known_hosts`. The GUI uses non-interactive SSH and does not
prompt for passwords or accept unknown/changed host keys. Connection errors
include the SSH diagnostic. No credentials or key files are copied into the database.

The remote machine needs Linux x86_64, glibc compatible with Ubuntu 22.04, Git in
its non-interactive PATH, and standard Linux utilities (`sh`, `sha256sum`, `mktemp`).
The same bundled Linux server and atomic SHA-256/version installation as WSL are
used, in the remote user's `~/.local/share/projlaunch/git-server/<sha256>/`.
Only missing/different/corrupted binaries are transferred. Installation requires
no root privileges, Rust toolchain or separately installed Git-server service.
RPC travels over SSH stdin/stdout, without an additional listening port. Each
host/username pair has a separate connection, and repository discovery runs on
the remote system. SSH host/user information is saved with tabs and their layouts.

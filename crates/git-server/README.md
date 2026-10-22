# Git server (local and WSL)

The launcher runs `project-launch-git-server stdio` as a separate process on demand. It is based
on the `jsonrpc-axum-probe` transport and `json-rpc-rs` dispatcher; the synthetic
Git handlers have been replaced by Git CLI reads. The fork stays pinned to
`226e48afcc9557adf9b2ce04c72a27a37858be15` (no upstream migration in this change).

`git-protocol` contains shared Rust DTOs, `git-client` owns the multiplexed stdio
connection, and `src-tauri/src/git_host.rs` exposes typed Tauri commands.
`src/src/domain/GitProtocol.fs` and `AppTauri.Git` are the frontend boundary. The launcher
UI and SQLite schema retain their existing responsibilities; this stage supplies
the API for the future graph/file/diff interface.

## Build and lifecycle

From the repository root:

```powershell
npm run build:git-server
# Windows build helper: .\build.bat build-git-server [--release]
cargo test --manifest-path crates/Cargo.toml --workspace --all-features --locked
cargo clippy --manifest-path crates/Cargo.toml --workspace --all-targets --all-features --locked -- -D warnings
cargo build --manifest-path src-tauri/Cargo.toml --locked
```

`npm run dev` prepares a debug sidecar; `npm run build` prepares an optimized
sidecar before compiling Fable and Vite. The implementation in `build/GitServer.fs` honors
`TAURI_ENV_TARGET_TRIPLE`, uses an explicit target/output directory, and places
the binary in `src-tauri/binaries/project-launch-git-server-<target>[.exe]`. The npm command invokes
`build.bat build-git-server`, forwarding arguments such as `--release`. The batch
script rebuilds the F# helper when needed. Tauri's
`bundle.externalBin` copies it next to the app. Generated binaries are ignored.
Direct Cargo builds of the Tauri host require `npm run build:git-server` first.
The portable ZIP made by `build.bat publish` (also `build.bat build --release`)
contains `projLaunch.exe` at its root and both target-suffixed Git servers under
`binaries/`. Runtime lookup prefers `binaries/project-launch-git-server-<target>[.exe]`
next to the GUI, then tries the legacy adjacent `project-launch-git-server[.exe]`,
then the source checkout. Missing-server errors report all attempted paths.

### Ubuntu 22.04 binary from Windows/WSL

```powershell
.\build.bat build-git-server ubuntu22 --release
# Optional: choose an installed WSL distribution instead of its default.
.\build.bat build-git-server ubuntu22 --release --wsl-distro Ubuntu
# The npm wrapper also forwards these options.
npm run build:git-server -- ubuntu22 --release
```

The build helper runs `build/scripts/build/git-server-ubuntu22.sh` through
`wsl.exe --exec bash` on Windows, or directly with Bash on Linux. The selected
Linux environment needs Rust/Cargo/rustup, the `x86_64-unknown-linux-gnu` standard
library (added by rustup if missing), Zig, and `readelf` from binutils. Cargo may
download locked dependencies. Rustup's `~/.cargo/env` is loaded automatically.
Zig is found using Linux `ZIG_BIN`, `PATH`, `~/bin/zig`, or `~/bin/zig*/zig`.
For example, configure `ZIG_BIN` in the WSL environment if several Zig versions
are installed; Windows executable paths are not suitable.

Following the vmssrv-mock Ubuntu build, the wrapper selects
`zig cc -target x86_64-linux-gnu.2.35` for both Rust linking and C dependencies.
It does not require an Ubuntu 22.04 build distribution. The build checks the
result's GLIBC symbol requirements and rejects anything newer than 2.35.
This targets x86_64 Linux with glibc 2.35 or later; it is not an Alpine/musl or
ARM64 binary, and symbol inspection does not replace testing on each supported OS.
The target version is fixed rather than inherited from the build host.

Cargo output is isolated in `crates/target/ubuntu22/`. The verified binary is
copied to `src-tauri/binaries/project-launch-git-server-x86_64-unknown-linux-gnu`,
alongside the native sidecar using the same target-suffixed naming convention.
Debug and release builds replace the same destination, just like the native build;
their Cargo outputs remain separate. Generated binaries are ignored and removed
by the existing clean command. Shell scripts are checked out with LF.
The native Tauri sidecar remains separate. The Linux output is included in the
portable release ZIP and deployed automatically to WSL endpoints. An Ubuntu build failure returns a nonzero
exit status and never copies the failed output over the last verified binary.

Build helper regression checks (after `dotnet build build/build.fsproj -f net11.0`):

```powershell
dotnet fsi --reference:build/bin/Debug/net11.0/build.dll build/scripts/GitServer.Tests.fsx
dotnet fsi --reference:build/bin/Debug/net11.0/build.dll build/scripts/Publish.Tests.fsx
```

Tauri starts the server lazily and checks the protocol version. No server runs
until a Git command is invoked. `git_disconnect` and application exit close stdin,
wait for the server to cancel and reap Git processes, and then reap the server.
Connection failure rejects pending calls. The next connection gets new repo IDs;
callers must reopen repositories and discard old history cursors. Reads are not
silently retried. Server stderr is diagnostic output; stdout is exclusively RPC.

## WSL lifecycle and endpoint routing

The existing project `Remote::Wsl(distribution)` maps to an endpoint
`{"kind":"wsl","distribution":"Ubuntu"}`. Tauri Git calls accept an optional
`endpoint` alongside `params`; omitted/null or `{"kind":"local"}` means local.
An endpoint is an execution environment, not a filesystem path. Each has a
separate client/process and session-scoped repository IDs. Workspace JSON v2
persists the tab endpoint and active endpoint; old v1 local snapshots still load.
The SQLite schema and project remote encoding are unchanged.

`src-tauri/src/wsl.rs` looks for the Linux binary in packaged `binaries/`, then
in the checkout. The bootstrap script checks its SHA-256 and runs
`--version-json`, which reports `serverVersion`, `protocolVersion`, and `target`
without requiring Git. The installation key is the complete binary hash, so
same-version development builds also update. Copying uses stdin and a temporary
file followed by atomic rename under the WSL user's
`~/.local/share/projlaunch/git-server/<sha256>/`. Existing versions are retained
for other app instances; no global daemon is installed. A bootstrap invocation
has a 60-second deadline. The supplied Linux build targets x86_64 glibc.

RPC runs directly over the spawned WSL process's stdin/stdout. Git must be on
PATH inside that distribution. Shutdown closes stdin and reaps the transport;
EOF makes the server cancel its Git children and exit. WSL itself is not stopped.
`project.discover` uses `git-discovery`, shared with local card discovery, to
resolve directories and workspace/solution files inside the server environment.

Opt-in real WSL regression (after building the Linux sidecar):

```powershell
$env:PROJLAUNCH_TEST_WSL_DISTRO = 'Ubuntu'
cargo test --manifest-path src-tauri/Cargo.toml --lib wsl_install_reuse_repair_rpc_and_shutdown --locked -- --ignored
```

It uses a disposable `/tmp` directory, checks install/reuse/repair, version
selection and protocol rejection, Unicode/quoted paths, discovery, status,
history, diff, reconnect and process exit. It does not read the app database.
Frontend routing/persistence regression: `node scripts/tests/test-wsl-endpoints.mjs`
after compiling Fable.

## Protocol v1

Each request/response is one UTF-8 JSON object followed by LF (NDJSON). Requests
use named object parameters. All operations require an ID; valid notifications
have no side effects. Batch requests return `-32005`.

| Method | Parameters | Result |
| --- | --- | --- |
| `server.hello` | `{}` | `serverVersion`, `protocolVersion`, `gitVersion`, `capabilities` |
| `project.discover` | `path`, `operationId` | `roots`, `warnings` (paths resolved in the server environment) |
| `repo.open` | `path`, `operationId` | `repoId`, `root`, `gitDir`, `commonDir` |
| `git.status` | `repoId`, `operationId` | HEAD/branch/upstream, ahead/behind, entries |
| `git.branches` | `repoId`, `operationId` | all local/remote branch tips, current branch and HEAD |
| `git.log` | `repoId`, `operationId`, optional `cursor`, `limit` | commits and optional `nextCursor` |
| `git.diffFiles` | `repoId`, `operationId`, `target` | paths, rename source and status |
| `git.diff` | `repoId`, `operationId`, `target`, `path` | text, binary/truncated/untracked flags |
| `git.cancel` | `operationId` | whether an active operation was found |

`operationId` is a caller-generated unique string (1–128 bytes), independent of
the JSON-RPC request ID. Cancellation is scoped to the connection. A cancellation
response acknowledges the signal; the operation's `-32021` response follows
after the child has exited. Duplicate active IDs are rejected.

Example exchange (keep stdin open while awaiting responses):

```json
{"jsonrpc":"2.0","id":1,"method":"server.hello","params":{}}
{"jsonrpc":"2.0","id":2,"method":"repo.open","params":{"path":"E:/Projects/projLaunch","operationId":"open-1"}}
{"jsonrpc":"2.0","id":3,"method":"git.status","params":{"repoId":"<repoId from response 2>","operationId":"status-1"}}
{"jsonrpc":"2.0","id":4,"method":"git.log","params":{"repoId":"<repoId>","operationId":"log-1","limit":100}}
{"jsonrpc":"2.0","id":5,"method":"git.diffFiles","params":{"repoId":"<repoId>","operationId":"files-1","target":{"kind":"unstaged"}}}
{"jsonrpc":"2.0","id":6,"method":"git.diff","params":{"repoId":"<repoId>","operationId":"patch-1","target":{"kind":"unstaged"},"path":"README.md"}}
```

`repo.open` accepts an absolute directory inside a worktree and resolves its root.
Separate worktrees get separate IDs. Bare repositories are rejected. One process
supports multiple repositories without needing the launcher's database.

Status uses porcelain v2 with NUL framing. Each entry has independent `index` and
`worktree` codes (`.` means unchanged), `kind` (`tracked`, `renamed`, `conflict`,
`untracked`), `submodule`, `path` and optional `originalPath`. Paths containing
spaces, tabs and newlines are not split or unquoted. Unicode is preserved;
non-UTF-8 paths are rejected explicitly in protocol v1.

`git.branches` reads all `refs/heads` and `refs/remotes` independently of log
pagination. Each entry includes `name`, full `refName`, `oid`, `isRemote`,
`isCurrent`, optional `upstream` and Git's `tracking` text (ahead/behind or gone).
Symbolic aliases such as `origin/HEAD` are omitted. The result also includes
`head` and `currentBranch`: detached HEAD has no current branch, while an unborn
branch has a name and no HEAD OID. The list reflects current refs and follows the
usual cancellation, output and response-frame limits. It performs no checkout.

History is topological across local branches, remote branches, commit tags and
HEAD, with OID, parent OIDs, author name/email, ISO dates, subject and refs. A new
history query freezes its starting OIDs and ref labels. Each cursor retains that
snapshot and offset, so new commits/ref movement do not shift subsequent pages.
Pages default to 100, maximum 200; cursors expire after 5 minutes or eviction.
Objects removed by pruning can invalidate a snapshot; refresh history in that
case. This first version reruns the bounded `git log --skip` traversal per page.

Diff targets:

- `{ "kind": "unstaged" }`: index → worktree.
- `{ "kind": "staged" }`: HEAD → index, including unborn HEAD.
- `{ "kind": "commit", "oid": "<full OID>", "parent": 1 }`: parent → commit.
  Parent is one-based; required for merge commits, optional for a single parent,
  absent for root commits. Root diff uses the empty tree for the repo's object format.
- `{ "kind": "untracked" }`: separate file list and plain-text preview.

Fetch the file list before asking for a patch. The server checks membership and
resolves rename sources itself. Patches are unified diffs, capped at 128 KiB with
`truncated: true`. Binary tracked files produce Git's binary marker; binary
untracked files return `binary: true, text: null`. Untracked symlinks display their
link target. Working-tree/index reads reflect live files; refresh if they change
between listing and selection. Conflict paths use Git's combined conflict patch;
the separate conflict status remains available in `git.status`.

## Bounds and scope

Git commands use argument arrays, literal pathspecs, disabled external diff and
textconv, no terminal prompting, and optional index locking disabled. There are
8 Git process slots per server, 32 ordinary requests plus 4 reserved cancellation
requests per connection, and 16 queued responses. Frames are capped at 1 MiB;
oversize responses return `-32004`, malformed/oversize input or overload can close
the connection. Each process has a 30-second timeout, 8 MiB ordinary output cap
and 64 KiB stderr cap. A timeout, cancellation or output limit kills and waits for
the child. Patch truncation also stops and reaps its child. Client requests have
a 90-second deadline; expiry closes the connection. Stream writes have 5 seconds.

Each connection holds up to 128 repositories and 128 cursors. History is limited
to 256 distinct starting OIDs to fit Windows command-line limits. These are
explicit errors/refresh conditions, never silent partial results.

No Git mutation methods, watchers, SSH bootstrap, TLS or authentication are
implemented. The prototype HTTP/WS
adapter remains behind the optional `network` feature for development on loopback;
it is not enabled in the sidecar build and is not a production remote endpoint.

The automated checks use disposable repositories and real child processes:
empty/unborn repositories, staged+unstaged changes, renames, Unicode/literal
paths, root/merge history and diff, conflicts, detached HEAD, binaries, truncation,
worktrees, reconnect invalidation, snapshot pagination, RPC framing, session
isolation, cancellation, timeout, output limits and shutdown.

Implementation references: [Git status](https://git-scm.com/docs/git-status),
[Git diff format](https://git-scm.com/docs/diff-format),
[Git log](https://git-scm.com/docs/git-log),
[Tokio child processes](https://docs.rs/tokio/latest/tokio/process/struct.Child.html),
[Tauri sidecars](https://v2.tauri.app/develop/sidecar/).

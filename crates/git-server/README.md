# Local Git server

The launcher runs `project-launch-git-server stdio` as a separate process on demand. It is based
on the `jsonrpc-axum-probe` transport and `json-rpc-rs` dispatcher; the synthetic
Git handlers have been replaced by Git CLI reads. The fork stays pinned to
`226e48afcc9557adf9b2ce04c72a27a37858be15` (no upstream migration in this change).

`git-protocol` contains shared Rust DTOs, `git-client` owns the multiplexed stdio
connection, and `src-tauri/src/git_host.rs` exposes typed Tauri commands.
`src/GitProtocol.fs` and `AppTauri.Git` are the frontend boundary. The launcher
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
Keep `project-launch-git-server.exe` alongside `projlaunch.exe` when copying a Windows build.

Tauri starts the server lazily and checks the protocol version. No server runs
until a Git command is invoked. `git_disconnect` and application exit close stdin,
wait for the server to cancel and reap Git processes, and then reap the server.
Connection failure rejects pending calls. The next connection gets new repo IDs;
callers must reopen repositories and discard old history cursors. Reads are not
silently retried. Server stderr is diagnostic output; stdout is exclusively RPC.

## Protocol v1

Each request/response is one UTF-8 JSON object followed by LF (NDJSON). Requests
use named object parameters. All operations require an ID; valid notifications
have no side effects. Batch requests return `-32005`.

| Method | Parameters | Result |
| --- | --- | --- |
| `server.hello` | `{}` | `protocolVersion`, `gitVersion`, `capabilities` |
| `repo.open` | `path`, `operationId` | `repoId`, `root`, `gitDir`, `commonDir` |
| `git.status` | `repoId`, `operationId` | HEAD/branch/upstream, ahead/behind, entries |
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

No Git mutation methods, watchers, graph UI, SSH/WSL bootstrap, remote connection
settings, TLS or authentication are added in this stage. The prototype HTTP/WS
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

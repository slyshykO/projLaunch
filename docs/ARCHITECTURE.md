# Architecture and Storage

[Documentation index](README.md) · [Project overview](../README.md) · [Development entry point](../AGENTS.md)

Source paths below are relative to the repository root.

## Technology Stack

The current app is built with:

- F# + Fable for the frontend application logic
- Elmish + Feliz + Feliz.DaisyUI for the UI
- Tauri 2 + Rust for desktop integration and process launching
- Vite for the frontend dev/build pipeline
- Paket for .NET package management

## Repository Layout

The Git read API runs in a separate Rust process on the local, WSL or SSH endpoint,
using stdio (over SSH for remote hosts). It provides
real status, paginated history and file diffs, with typed Tauri/Fable wrappers.
See [the Git server guide](../crates/git-server/README.md) for methods, build steps,
lifecycle, limits and tests. Repository tabs use this API for the history graph,
changed files and diff views.

- `src/` - frontend project, HTML/CSS entry point and assets
- `src/src/` - F# application entry point, state and debug fixtures
- `src/src/common/`, `src/src/interop/` - shared helpers and browser/JavaScript bindings
- `src/src/tauri/` - Tauri bindings and typed application IPC calls
- `src/src/domain/`, `src/src/ui/` - project/Git data types and UI components
- `src-tauri/` - Rust/Tauri host application and native commands
- `crates/` - Cargo workspace for `git-protocol`, `git-server`, `git-client` and `git-discovery`
- `src-tauri/src/storage/` - SQLite worker, project queries, schema migration and legacy import
- `src-tauri/migrations/` - versioned SQL schema
- `src-test/` - xUnit test project
- `build/` - custom F# build helper used by `build.bat`
- `serde-probe-lib/` - standalone serialization probe library
- `docs/` - user guides and technical reference
- `blueprints/` - plans, their implementation records, future backlog and frozen historical handoff

The frontend calls typed `AppTauri.Git` wrappers; Tauri owns server processes and
connections. Repositories are identified by endpoint and worktree root. Runtime
repository IDs and cursors belong to one connection and are reopened after a
reconnect. Git discovery reads directories, VS Code workspaces and solutions in
the selected endpoint; persistent project/repository bindings are not stored.

Whole-file staging uses typed `git_stage` / `git_unstage` IPC and
`git.stage` / `git.unstage` RPC. The frontend tracks in-flight writes by endpoint
and worktree, independently of tab lifetime, and defers watcher refresh until
completion. The server validates current file-group membership and supplies
literal NUL-separated paths to one Git command. Git owns index locking;
mutations are not retried automatically. Both successful and failed writes
trigger fresh status/files/diff reads. This transient state adds no database
migration or workspace JSON fields.

Detailed UI behavior, Git subscriptions and workspace restoration are described
in the [user guide](USER-GUIDE.md). WSL/SSH installation and transport are covered
in [remote repositories](REMOTE-REPOSITORIES.md).

## Project Storage

Saved projects are stored in `projlaunch.db` in the Tauri app data directory
(`%APPDATA%/com.projlaunch.app` on Windows). The Rust host uses `rusqlite` with
bundled SQLite. A dedicated worker owns the connection; frontend commands wait
for initialization without running database work on the UI thread.

Only one instance can write to a given database. The `database_owner` table
records its PID, process start time, executable path and a unique ownership token.
Startup checks the recorded process identity. An exclusive OS lock on the adjacent
`projlaunch.writer.lock` file prevents simultaneous claims and is released by the
OS even after a crash or force kill. A leftover database marker is safely replaced
by the next lock holder; PID reuse cannot keep the database locked indefinitely.
The lock file stays on disk and must not be deleted while instances are running.

Other instances open SQLite with read-only flags and skip migrations/imports.
They can open projects in IDEs, read Git history and change their local tabs/panels,
but cannot edit projects or save last-opened timestamps or workspace state. The UI
shows the read-only mode and owner PID. Close the owning instance and restart a
reader to acquire write access. Normal exit drains prior storage operations and
removes only that instance's ownership marker before releasing the lock.

On the first launch with SQLite storage, the app creates the schema and imports
all regular files directly inside the old `projects/` directory, including
extensionless files. The original files are left untouched. Both older Thoth
remote arrays and current serde remote objects are accepted, as are records
without a remote field. IDs, dates, paths and environment values are preserved.

The import and its `legacy_projects_v1` completion marker are committed in one
transaction. Malformed/unreadable files, duplicate IDs or conflicts with existing
database records abort the entire import. The error, including the affected path,
is displayed in the application. Fix the reported problem and restart to retry;
project reads and writes remain blocked after an initialization failure.

After a successful import, the database is the sole working storage. Old files
are retained as a snapshot and are neither updated nor imported again. A fresh
installation without a `projects/` directory starts with an empty database and
records the import as complete. An existing database without the completion
marker still attempts import. Schema versions use SQLite's `user_version`;
newer unsupported versions are rejected rather than modified.

Do not delete `projlaunch.db` to retry an import after it has succeeded: it may
contain newer changes or projects that are absent from the old files. Reverting
to an older, JSON-based ProjLaunch version likewise shows the old snapshot.

Each record includes:

- `id`
- `name`
- `lastOpened`
- `description`
- `path`
- `ide`
- `environment`
- `remote`

`remote` supports either:

- `Ssh { host, username }`
- `Wsl "<distro>"`

## Logging

Rust and frontend logs are written to `projlaunch.YYYY-MM-DD.log` next to
`projlaunch.db` in the app data directory (`%APPDATA%/com.projlaunch.app` on
Windows). Rotation uses UTC dates and retains the latest 7 log files. Each entry
includes a timestamp, level and source (`frontend` for UI messages). Rust output
also remains available on stderr; the WebView developer console still works.

The frontend forwards `console.log/info/warn/error/debug/trace`, uncaught
JavaScript errors, unhandled promise rejections and Elmish errors. Ordinary
messages are sent automatically with a 50 ms batching delay; warnings/errors
start sending immediately, unless another batch is already in flight. The file
writer runs continuously on a separate thread. Closing the app only drains the
remaining messages; logging does not wait for shutdown.

`FrontendLog.info`, `FrontendLog.debug` and `FrontendLog.error` are available to
F# code. IPC failures log the command name and error, never the argument object.
Filesystem errors include the affected file or directory and retain the original
cause. This covers project discovery, file previews, Git/IDE launch, log creation,
and SQLite initialization, reads, writes and ownership locks.
State diagnostics go directly to the log; About does not display an error history.
Actionable project-loading and editor errors remain visible in their respective UI.
Avoid logging project records, environment values or credentials. Browser-only
development keeps console output without attempting native logging.

The default level is `info`. Set `PROJ_LAUNCHER_LOG=debug` (or, for example,
`info,frontend=debug`) before launching to include debug messages. Invalid filter
values fall back to `info`. Each frontend batch is limited to 32 entries, each
message to 4,000 JavaScript characters (at most 16 KiB in UTF-8). The frontend
queue holds 200 messages and reports overflow; the native writer has a bounded
4,096-line queue and can drop messages under overload. If file initialization
fails, the app continues with stderr/developer-console output and reports the
logging error there. A stalled log transport delays closing by at most 1.5 seconds.

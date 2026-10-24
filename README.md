# projlaunch

`projlaunch` is a Tauri desktop app for keeping a small catalog of development projects and opening them quickly in your preferred IDE.

For development context, completed work and the next steps across machines,
start with [CODEX-HANDOFF.md](CODEX-HANDOFF.md).

The current app is built with:

- F# + Fable for the frontend application logic
- Elmish + Feliz + Feliz.DaisyUI for the UI
- Tauri 2 + Rust for desktop integration and process launching
- Vite for the frontend dev/build pipeline
- Paket for .NET package management

## What The App Does

The app currently lets you:

- view saved projects in a grid sorted by `lastOpened`
- add a project from the UI with name, description, IDE, and solution/workspace path
- edit a project using the pencil button at the top right of its card, including SSH/WSL and environment variables
- see a Git icon and clean/uncommitted status for each associated local, WSL or SSH repository at the bottom of a card
- open projects in:
  - VS Code
  - Visual Studio 2022
  - Visual Studio 2026
  - Rider
- launch VS Code projects through remote targets:
  - SSH
  - WSL
- persist window state between launches
- work in desktop tabs: a permanent Projects tab and closable repository tabs
- open a repository tab by clicking its Git indicator; shared roots in the same endpoint reuse the same tab
- inspect app/runtime details in the `Help → About ProjLaunch` dialog

The native menu contains File (Add project, Close repository tab, Exit), View
(Projects, Refresh Git status), and Help (About). Add project also supports
Ctrl/Cmd+N. About closes with Close or Escape. Tab navigation supports the arrow,
Home and End keys. Switching tabs retains each panel's scroll position.
Repository tabs load commit history from the Git server in the selected local/WSL/SSH endpoint: subject, author,
commit date, short hash and branch/tag labels. History loads in pages of 100;
`Load more` appends the next page. `Refresh`, the View menu refresh action and
window focus reload the active repository from a fresh snapshot. Failed reads
keep previously loaded rows visible; Refresh also recovers from expired cursors
or a restarted server.

Git updates also arrive automatically from filesystem subscriptions in each
repository's local, WSL or SSH server. One subscription is shared by project
cards and tabs for the same endpoint/worktree. Working-tree and index edits
refresh status and the selected mutable diff, including further edits to an
already dirty file. HEAD, branch, tag and configuration changes also refresh
history and branches. Linked worktrees observe their separate index/HEAD and
shared refs. Bursts are coalesced over 300 ms; changes arriving during a read
are retained for another update.

Automatic history refresh keeps the loaded depth, selection and scroll anchor;
the old rows remain visible until the replacement pages are ready. Background
tabs defer reads until selected. Closing the last card/tab reference releases
its subscription. Disconnects retry with a bounded backoff and invalidate views
after reconnecting. Watch errors show a warning; manual Refresh and focus refresh
remain available. Observation depends on native filesystem notifications; some
network/shared mounts may not report external changes reliably. Ignored build
outputs can cause coalesced status checks, but do not reload history.

A left sidebar lists all local and remote-tracking branches, independently of
loaded history pages. Filter names, switch between a flat list and a tree grouped
by `/`, or collapse individual groups. The current branch is marked; full refs,
upstream and tracking information are available in tooltips. Remote folder tooltips
also show the configured fetch URL. Detached HEAD and
branches without a first commit have explicit messages; symbolic remote HEAD
aliases are omitted. Selecting a branch reveals its tip in the graph, loading
history pages as needed, and opens its changed files. Cancel stops further reads;
errors preserve the current selection and allow retry. Selection never checks out
a branch. Refresh and window focus reload the list; failures keep prior branches
visible with an error. The sidebar can be hidden or resized by dragging its right
edge, using arrow keys (Shift for larger steps), Home/End, or double-click to reset.
Sidebar width, visibility, filter and tree expansion remain local to the mounted
tab; they are not saved across restarts. File panels adapt to the remaining width.

Select a commit row (or focus its subject and press Enter/Space) to load changed
files through the Git server. The file panel shows change types and old/new paths
for renames. Parents are shown as `parent: abc123,def456`. Merge commits initially
compare with the first parent; the compact `diff:` selector switches parents.
Click a parent hash in the file panel or diff header to select that commit and
reveal its graph row. Missing pages load automatically; Cancel stops the search
after the current read. A missing parent or failed read leaves the current selection
intact; click the hash again to retry from a fresh history snapshot. Initial commits compare
against an empty tree. Loading, empty results and errors are shown separately;
Retry reopens the repository after a server restart. Selection is independent per
tab and stays visible when refreshing history, even outside its new first page.
The file panel sits beside history in wide windows and below it in narrow windows.
Select a changed file to open its unified diff in place of history. Added and
removed lines have colored backgrounds. The file list stays available for switching
files; `✕` returns to the mounted history panel with its scroll position.
File markers are `M` (modified), `+` (added), `-` (deleted), `R` (renamed),
`C` (copied) and `T` (type changed), with full descriptions in tooltips.
Changing the commit or merge parent closes the old diff. Diff state stays independent
per tab; loading, retry, binary files, empty patches and server truncation are explicit.
The Branch / Tag column sits to the left of Graph, with horizontal connectors
from labels to their commit nodes. Actual refs are always visible; multiple refs
stack in the same cell, and long names truncate with full refs in tooltips.
Commit messages occupy their own column to the right of the graph.
The Graph column draws colored parent connections alongside each commit. Merge
commits use double-circle nodes; the selected commit has an outer ring. Lines
follow wrapped row heights and continue across loaded pages without rerouting
previous rows. Dashed tails mark parents outside the loaded history. Colors
identify routes within the current snapshot, not permanent branch identities.
Hover or keyboard-focus a commit to highlight its line. Rows without refs reveal
the line's branch name in a dashed badge in the left column, together with its
connector; selection keeps that name visible without changing the row height.
Branch ref badges use the graph color and short names, with full refs in tooltips.
Names come from the topmost branch ref on each line (local preferred over remote
at the same commit). Tags and remote HEAD refs do not name lines. For a deleted
branch, a standard merge subject can supply a hint marked `~`; a real ref takes
precedence. A name may resolve when loading more history without moving lines.
These labels describe graph lines, not every branch containing a commit or the
branch on which it was originally created.
Open repository tabs, their order, the active tab (including Projects), the selected
commit/merge parent or working-tree changes, and the open diff file are restored
after restarting. Each repository remembers its file-panel width in wide windows
and height in narrow windows. Drag the divider to resize it; arrow keys move it,
Shift increases the step, Home/End select the limits and double-click resets it.

In the writable instance, workspace state is saved automatically in `projlaunch.db`. Closing waits for the
latest save; a failed save keeps the window open and shows Retry. A failed restore
also shows Retry and leaves the stored session untouched. The SQLite schema is
upgraded to version 3 without changing project records or legacy imports.
Restored tabs receive new runtime IDs and reload Git data: the active repository
loads immediately, background repositories load when selected. Missing repositories
remain as tabs with retryable errors. A missing saved diff file falls back to the
file list. Each tab also saves its loaded commit count and history scroll position,
including horizontal scroll and the first visible commit with an offset inside its row.
On restart, a fresh Git snapshot loads sequential pages until the saved count or the
end of history. The visible commit anchors the scroll position even when preceding
rows have moved; if it is absent from the reloaded pages, the saved pixel offset is
clamped to the available history. History hidden behind a restored diff waits until
shown before applying its scroll position. A selected older commit can still be inspected.

Switching away pauses automatic page loading after the current read; selecting the
tab resumes it. Cancel, scrolling or choosing a commit/branch takes precedence over
restoration. Errors retain the saved depth and position; Refresh retries from a new
snapshot. Closing during restoration preserves its target for the next launch.
Ordinary Refresh after restoration still starts at the first page. Scroll saves are
debounced, while closing drains the latest position through the existing workspace save.
Workspace JSON v3 reads older v1/v2 sessions without history state; the SQLite schema
remains version 3. Patches, commit-page contents and transport IDs/cursors are not saved.

Repositories with local changes show an `Uncommitted changes` row above history.
Its hollow node connects to the current HEAD with a dashed line, including when
other branch tips precede HEAD. Repositories without a first commit show the node
without a parent. Selecting it opens Staged (HEAD → index), Unstaged (index →
working tree) and Untracked file groups in the existing file panel. A partially
staged file can appear in both groups, with a separate diff for each. New files
show a text preview; binary and truncated results retain their explicit messages.
Refresh and window focus reload the status and selected working-tree file groups,
closing any mutable diff so stale contents are not retained. Committed selections
and their diffs remain unchanged. Clean repositories omit the extra graph row;
status failures are displayed separately from history errors. No staging or
other Git write operations are performed.

When a project is opened, the app updates its `lastOpened` value and rewrites the stored project record.

Git indicators refresh after loading or saving projects and when the app regains
focus. For a directory or ordinary project file, the containing repository is used.
For `.code-workspace`, `.sln` and `.slnx`, explicitly listed folders/projects are
also checked, with duplicate worktree roots shown once. Child directories are not
scanned. Hover over an indicator for its full path, branch or error details.
Paths in workspace/solution files resolve relative to those files; the card's own
path must be absolute. Untracked, staged and unstaged files count as changes.
Failed checks are shown as unavailable, never as clean.

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

## Current Limitations

- Deleting projects from the UI is not implemented yet.
- Remote launch options are only wired up for VS Code.
- Remote Git currently supports x86_64 Linux with glibc (Ubuntu 22.04 or compatible); remote Windows, macOS and ARM64 binaries are not supported.
- Visual Studio launch paths are currently hard-coded for Windows installs.
- The test project exists, but it only contains a placeholder test right now.

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

## Repository Layout

The local Git read API now runs in a separate Rust process over stdio. It provides
real status, paginated history and file diffs, with typed Tauri/Fable wrappers.
See [the Git server guide](crates/git-server/README.md) for methods, build steps,
lifecycle, limits and tests. Repository tabs use this API for the history graph,
changed files and diff views.

- `src/` - frontend project, HTML/CSS entry point and assets
- `src/src/` - F# application entry point, state and debug fixtures
- `src/src/common/`, `src/src/interop/` - shared helpers and browser/JavaScript bindings
- `src/src/tauri/` - Tauri bindings and typed application IPC calls
- `src/src/domain/`, `src/src/ui/` - project/Git data types and UI components
- `src-tauri/` - Rust/Tauri host application and native commands
- `crates/` - Cargo workspace for `git-protocol`, `git-server` and `git-client`
- `src-tauri/src/storage/` - SQLite worker, project queries, schema migration and legacy import
- `src-tauri/migrations/` - versioned SQL schema
- `src-test/` - xUnit test project
- `build/` - custom F# build helper used by `build.bat`
- `serde-probe-lib/` - standalone serialization probe library

## Requirements

For local development, this repo expects:

- Node.js + npm
- .NET SDK 11 (the RC2 SDK selected by `global.json`, or a newer compatible SDK)
- .NET 10 runtime for the local Paket tool
- Rust toolchain
- Git on PATH (used by the local Git server)
- Tauri prerequisites for your platform

On Windows, you will also want the IDEs you plan to launch installed and available on disk/path.

The .NET projects target both `net10.0` and `net11.0`. The local tool manifest pins
Paket `12.0.0-alpha001`, which adds .NET 11 framework support; Paket 11.0.1 cannot
restore `net11.0` projects. Run `dotnet tool restore` after pulling tool updates.

## Development

Install/restore dependencies as needed:

```powershell
npm install
dotnet tool restore
dotnet paket restore
```

Start the frontend + Tauri dev workflow:

```powershell
npm run tauri dev
```

Useful npm scripts:

```powershell
npm run dev
npm run start
npm run build
```

Notes:

- `npm run dev` starts Fable in watch mode and runs Vite in development mode.
- `npm run start` is a lighter watch/serve variant.
- `npm run build` compiles the frontend into `dist/`.

## Build And Maintenance

The repo also includes a custom Windows helper:

```powershell
.\build.bat self-build
.\build.bat build
.\build.bat build --release
.\build.bat publish
.\build.bat publish --wsl-distro Ubuntu
.\build.bat run
.\build.bat run --release
.\build.bat clean
.\build.bat update
.\build.bat build-daisyui
.\build.bat build-git-server
.\build.bat build-git-server ubuntu22 --release
.\build.bat build-git-server ubuntu22 --release --wsl-distro Ubuntu
.\build.bat version
.\build.bat set-version 0.5.2
.\build.bat help
```

What these do:

- `self-build` publishes the build helper as a native Windows x64 executable and exits
- `build` restores npm dependencies from `package-lock.json` with `npm ci --include=dev`, syncs Feliz.DaisyUI source references in `src/index.css`, and then runs `npm run tauri build -- --debug` (debug mode by default)
- `build --release` builds the Windows GUI, native Git server and Ubuntu 22 Git server,
  then creates the same portable ZIP as `publish`. Requires Windows x64 and WSL
  with the Linux build prerequisites below.
- `publish` builds that release ZIP; `--wsl-distro NAME` selects the Linux build
  distribution (otherwise the WSL default). Output:
  `build/dist/projLaunch-<version>-windows-x64.zip`.
- `run` performs the same build as `build`, then launches the debug app; `run --release` builds and launches the release app. It waits until the app exits and does not launch it if the build fails.
- `clean` removes generated artifacts across all projects and build variants, including the build helper itself
- `update` runs npm/cargo update checks
- `build-daisyui` builds a local `Feliz.DaisyUI` checkout if present
- `build-git-server` builds and copies the Git sidecar; add `--release` for optimized output
- `build-git-server ubuntu22` builds a separate Linux x86_64 server with Zig targeting
  glibc 2.35 (Ubuntu 22.04). On Windows it runs through WSL; `--wsl-distro NAME`
  selects a distribution, otherwise the WSL default is used. The build distribution
  may be newer than Ubuntu 22.04. On Linux the same script runs directly.
  Add `--release` for optimized output. See the [Git server guide](crates/git-server/README.md)
  for prerequisites and output paths. This is an explicit build target; ordinary
  app/native sidecar builds do not require WSL.
- `set-version X.Y.Z` updates project versions, creates a `Release vX.Y.Z` commit,
  and creates the local Git tag `vX.Y.Z`. The version must exceed every existing
  local release tag (`vX.Y.Z` or `X.Y.Z`), compared numerically. It does not push.
- `version` prints the current Git version as one line, using the same format as
  About (`X.Y.Z`, optional commit distance/hash, and `+` for tracked changes).
  It does not synchronize manifests, commit or tag. Extra arguments are rejected.

Git server build commands use explicit stdin/stdout/stderr pipes, including WSL.
Output is forwarded to the current terminal; failures also report the hexadecimal
exit code and preserve diagnostics from commands whose stdout is captured.

Both `publish` and `build --release` produce this extraction layout, with no extra
top-level directory:

```text
projLaunch.exe
binaries/
  project-launch-git-server-x86_64-pc-windows-msvc.exe
  project-launch-git-server-x86_64-unknown-linux-gnu
```

The GUI first looks for its platform's server in `binaries/` next to itself.
Legacy adjacent-server and source-checkout paths remain fallback locations.
The Linux server is deployed automatically for WSL and SSH endpoints; local Windows
repositories use the Windows server. This is a Windows GUI distribution, not a Linux GUI build.
The destination Windows system still needs WebView2 Runtime and Git on PATH.
The archive contains only the GUI and two servers, without symbols or user data.
It replaces the previous archive of the same version only after both builds and
ZIP creation succeed. Publish currently uses standard Cargo output paths; unset
`CARGO_TARGET_DIR` and `CARGO_BUILD_TARGET` overrides before invoking it.

Versions come from the nearest reachable release tag (`vX.Y.Z` or `X.Y.Z`),
using `git describe --tags`. The version is synchronized into every tracked
`package.json`, `tauri.conf.json`, explicit Cargo package/workspace version, and
the matching local-package entries in npm/Cargo lockfiles. Rust workspace members
keep `version.workspace = true`; dependency versions are preserved.

`build`, `run`, `build-git-server`, and `build/scripts/version/version.fsx` synchronize
versions before building when tracked files or the index have uncommitted
changes, without committing those changes or tagging. On a clean checkout, if the
committed project versions still contain a generated `.dirty` suffix, the helper
removes only that suffix from manifests and lockfiles and creates one local commit:
`Remove dirty markers from project versions`. It preserves the existing version,
distance and hash; it does not create a tag or push. Once the markers are removed,
clean builds leave the committed files unchanged. This prevents a
commit/build/version-update cycle. Untracked files alone
do not trigger synchronization, matching the tracked-change scope of Git describe.
The npm `pretauri` hook
also synchronizes before Tauri reads its configuration. Generated `version.fs` and
`Version.cs` retain the full Git description, including commit distance/hash and
the `+` suffix for tracked working-tree changes. All three manifest formats support
SemVer build metadata: a clean release tag produces `X.Y.Z`, subsequent commits
produce `X.Y.Z+N.gHASH`, and tracked source changes add `.dirty`. A dirty exact tag
produces `X.Y.Z+0.gHASH.dirty`. The same version is written to the lockfiles.
Version-only differences generated by synchronization do not mark the build dirty;
other changes in manifests and staged files do. If source changes are reverted while
version updates remain uncommitted, synchronization removes `.dirty` without creating
a commit. Generated `version.fs` and `Version.cs` always describe the current Git
state, including on a clean checkout with older committed manifest versions.
About calls the generated version function at runtime. It must not be a F#
`Literal`: Fable can retain an inlined old value in cached consumer JavaScript
when only the generated version file changes.
Automatic cleanup requires Git author/committer identity and successful commit
hooks. If the commit fails, the helper restores its file edits and reports an error;
the next clean build can retry. It never includes other staged or unstaged edits
in an automatic commit.
Build metadata identifies the source state but does
not change SemVer precedence.

Tauri's application version supports the full SemVer string. Installer-specific
version fields can have stricter rules: for MSI, `bundle.windows.wix.version`
requires a numeric `major.minor.patch.build` value and may need an explicit override
when enabling installer bundling (currently disabled).

Before `set-version`, commit or stash changes other than project-version updates.
Version-only changes left by a build are accepted. Existing tags (with or without
`v`), invalid release numbers and versions no higher than the maximum existing
release tag are rejected before writing files. All local release tags are checked,
including tags on other branches; the first release is allowed if none exist.
If no release
tag is reachable, builds fail with a Git/version error; create the initial release
with `set-version X.Y.Z` first. Only files tracked by Git participate in versioning.

`build.bat` also publishes the helper automatically when its executable is missing.
The helper checks embedded hashes of its sources, project files, and build configuration
at startup. If those inputs have changed, the batch file rebuilds it and retries the
original command once. Self-publishing requires the Windows Native AOT build tools
(Visual Studio C++ build tools and Windows SDK).

Run `.\build.bat clean` to remove build artifacts for all projects and all
configurations, frameworks and target platforms. This includes Rust `target`
trees (the app, Git workspace and repository root), .NET `bin`/`obj` and test
results, Fable output, frontend `dist`/`dist-ssr` and Vite caches, copied Git
sidecars, generated Tauri schemas and Serde codecs, and local DaisyUI build/package
output. Project discovery also includes nested projects and Paket source checkouts.
Installed dependencies (`node_modules`, Paket sources and global package caches),
source files, lockfiles and application data are preserved.

Cleaning runs directly in Windows PowerShell before starting the build helper;
it needs no .NET SDK, Rust or npm and also deletes the helper's own `bin`/`obj`.
The next build republishes the helper automatically. Close running builds,
watchers and the app first: failures to remove locked files are reported with a
nonzero exit code, while other output trees are still cleaned. For a preview,
run `powershell.exe -NoProfile -File scripts/clean.ps1 -WhatIf`.

After cloning, run `.\build.bat build` with the prerequisites above installed.
The command restores npm dependencies, including the build tools, before starting
Tauri. The frontend build then restores the local .NET tools and Paket dependencies.
If npm restore fails, the build stops before starting Tauri.

The app executable is written to `src-tauri/target/debug/projlaunch.exe` by default,
or `src-tauri/target/release/projlaunch.exe` with `--release`.

## Tests

Run tests with:

```powershell
dotnet test .\src-test\src-test.fsproj
```

The F# test suite is currently a placeholder. Run Rust storage tests with:

```powershell
cargo test --manifest-path src-tauri/Cargo.toml --lib
```

These use temporary directories, never the real app-data directory. They cover
first-run import, both remote formats, preservation of source files, restart and
retry behavior, transactional rollback, duplicate IDs, database conflicts,
concurrent startup and asynchronous worker commands.

Run the build helper's regression checks with:

```powershell
dotnet fsi --exec build/scripts/SelfBuild.Tests.fsx
dotnet fsi --exec build/scripts/Versioning.Tests.fsx
dotnet fsi --exec build/scripts/GitServerProcess.Tests.fsx
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/test-clean.ps1
```

Run the project editor's state and IPC regression checks after compiling the frontend:

```powershell
dotnet fable src -o ./src/bin/fable-js --configuration Debug
node scripts/tests/test-project-editor.mjs
node scripts/tests/test-project-git.mjs
node scripts/tests/test-about-version.mjs
node scripts/tests/test-git-watching.mjs
node scripts/tests/test-git-watch-lifecycle.mjs
node scripts/tests/test-project-menu.mjs
node scripts/tests/test-desktop-tabs.mjs
node scripts/tests/test-repository-history.mjs
node scripts/tests/test-commit-graph.mjs
node scripts/tests/test-commit-files.mjs
node scripts/tests/test-commit-diff.mjs
node scripts/tests/test-working-tree.mjs
node scripts/tests/test-workspace-state.mjs
node scripts/tests/test-history-restoration.mjs
node scripts/tests/test-history-scroll.mjs
node scripts/tests/test-wsl-endpoints.mjs
node scripts/tests/test-ssh-endpoints.mjs
node scripts/tests/test-parent-navigation.mjs
node scripts/tests/test-branches.mjs
node scripts/tests/test-storage-access.mjs
node scripts/tests/test-logging.mjs
```

These checks use an isolated IPC stub, without accessing the user's database.
They cover editing without duplicates, preserved IDs and timestamps, SSH/WSL and
environment values, cancellation, failed saves, retry and adding new projects.
The Git checks cover refresh races, stale responses, errors/recovery and rendered
indicators. Rust tests also exercise workspace/solution discovery and real stdio
status checks; build the Git sidecar first with `npm run build:git-server`.
History checks cover paging, duplicate requests, independent tabs, late replies
after closing a tab, empty repositories, failed reads and session recovery.
Commit file checks cover selection races, initial/merge commits, parent changes,
renames, escaped paths, failed reads/retry and selection across tabs/history refresh.
Diff checks cover exact paths/targets, selection races, close/reopen during loading,
retry, binary/empty/truncated patches, escaping and stale responses after tab closure.
Logging checks cover automatic runtime delivery, immediate errors/warnings,
bounded batches, Unicode/errors, transport failure recovery and draining before
native close. Rust logging tests write only to temporary directories.

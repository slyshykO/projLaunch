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
- see a Git icon and clean/uncommitted status for each associated local repository at the bottom of a card
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
- open a repository tab by clicking its Git indicator; shared roots reuse the same tab
- inspect app/runtime details in the `Help → About ProjLaunch` dialog

The native menu contains File (Add project, Close repository tab, Exit), View
(Projects, Refresh Git status), and Help (About). Add project also supports
Ctrl/Cmd+N. About closes with Close or Escape. Tab navigation supports the arrow,
Home and End keys. Switching tabs retains each panel's scroll position.
Repository tabs currently show their name and path as a home for the upcoming
history/graph/diff interface. Open tabs are kept only for the current app session.

When a project is opened, the app updates its `lastOpened` value and rewrites the stored project record.

Git indicators refresh after loading or saving projects and when the app regains
focus. For a directory or ordinary project file, the containing repository is used.
For `.code-workspace`, `.sln` and `.slnx`, explicitly listed folders/projects are
also checked, with duplicate worktree roots shown once. Child directories are not
scanned. Hover over an indicator for its full path, branch or error details.
Paths in workspace/solution files resolve relative to those files; the card's own
path must be absolute. Untracked, staged and unstaged files count as changes.
Remote SSH/WSL checks and failed checks are shown as unavailable, never as clean.

## Current Limitations

- Deleting projects from the UI is not implemented yet.
- Remote launch options are only wired up for VS Code.
- Git indicators currently support local repositories; SSH/WSL Git connections are not implemented.
- Visual Studio launch paths are currently hard-coded for Windows installs.
- The test project exists, but it only contains a placeholder test right now.

## Project Storage

Saved projects are stored in `projlaunch.db` in the Tauri app data directory
(`%APPDATA%/com.projlaunch.app` on Windows). The Rust host uses `rusqlite` with
bundled SQLite. A dedicated worker owns the connection; frontend commands wait
for initialization without running database work on the UI thread.

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
lifecycle, limits and tests. The graph/file/diff UI is a subsequent stage.

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
.\build.bat run
.\build.bat run --release
.\build.bat clean
.\build.bat update
.\build.bat build-daisyui
.\build.bat build-git-server
.\build.bat set-version 0.4.2
.\build.bat help
```

What these do:

- `self-build` publishes the build helper as a native Windows x64 executable and exits
- `build` restores npm dependencies from `package-lock.json` with `npm ci --include=dev`, syncs Feliz.DaisyUI source references in `src/index.css`, and then runs `npm run tauri build -- --debug` (debug mode by default)
- `build --release` performs the same preparation and runs `npm run tauri build` (release mode)
- `run` performs the same build as `build`, then launches the debug app; `run --release` builds and launches the release app. It waits until the app exits and does not launch it if the build fails.
- `clean` removes generated artifacts across all projects and build variants, including the build helper itself
- `update` runs npm/cargo update checks
- `build-daisyui` builds a local `Feliz.DaisyUI` checkout if present
- `build-git-server` builds and copies the Git sidecar; add `--release` for optimized output
- `set-version X.Y.Z` updates project versions, creates a `Release vX.Y.Z` commit,
  and creates the local Git tag `vX.Y.Z`. It does not push.

Versions come from the nearest reachable release tag (`vX.Y.Z` or `X.Y.Z`),
using `git describe --tags`. The version is synchronized into every tracked
`package.json`, `tauri.conf.json`, explicit Cargo package/workspace version, and
the matching local-package entries in npm/Cargo lockfiles. Rust workspace members
keep `version.workspace = true`; dependency versions are preserved.

`build`, `run`, `build-git-server`, and `scripts/version/version.fsx` synchronize
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
`v`) and invalid release numbers are rejected before writing files. If no release
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
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/test-clean.ps1
```

Run the project editor's state and IPC regression checks after compiling the frontend:

```powershell
dotnet fable src -o ./src/bin/fable-js --configuration Debug
node scripts/tests/test-project-editor.mjs
node scripts/tests/test-project-git.mjs
node scripts/tests/test-about-version.mjs
node scripts/tests/test-project-menu.mjs
node scripts/tests/test-desktop-tabs.mjs
node scripts/tests/test-logging.mjs
```

These checks use an isolated IPC stub, without accessing the user's database.
They cover editing without duplicates, preserved IDs and timestamps, SSH/WSL and
environment values, cancellation, failed saves, retry and adding new projects.
The Git checks cover refresh races, stale responses, errors/recovery and rendered
indicators. Rust tests also exercise workspace/solution discovery and real stdio
status checks; build the Git sidecar first with `npm run build:git-server`.
Logging checks cover automatic runtime delivery, immediate errors/warnings,
bounded batches, Unicode/errors, transport failure recovery and draining before
native close. Rust logging tests write only to temporary directories.

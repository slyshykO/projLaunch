# projlaunch

`projlaunch` is a Tauri desktop app for keeping a small catalog of development projects and opening them quickly in your preferred IDE.

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
- open projects in:
  - VS Code
  - Visual Studio 2022
  - Visual Studio 2026
  - Rider
- launch VS Code projects through remote targets:
  - SSH
  - WSL
- persist window state between launches
- inspect app/runtime details on the `About` page

When a project is opened, the app updates its `lastOpened` value and rewrites the stored project record.

## Current Limitations

- The add-project dialog does not expose editing or deleting existing entries yet.
- Remote launch options are only wired up for VS Code.
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

## Repository Layout

- `src/` - F# frontend application code compiled with Fable
- `src-tauri/` - Rust/Tauri host application and native commands
- `src-tauri/src/storage/` - SQLite worker, project queries, schema migration and legacy import
- `src-tauri/migrations/` - versioned SQL schema
- `src-test/` - xUnit test project
- `build/` - custom F# build helper used by `build.bat`
- `serde-probe-lib/` - local shared/library code referenced by the app

## Requirements

For local development, this repo expects:

- Node.js + npm
- .NET SDK 11 (the RC2 SDK selected by `global.json`, or a newer compatible SDK)
- .NET 10 runtime for the local Paket tool
- Rust toolchain
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
.\build.bat clean
.\build.bat update
.\build.bat build-daisyui
.\build.bat help
```

What these do:

- `self-build` publishes the build helper as a native Windows x64 executable and exits
- `build` restores npm dependencies from `package-lock.json` with `npm ci --include=dev`, syncs Feliz.DaisyUI source references in `src/index.css`, and then runs `npm run tauri build`
- `clean` runs `cargo clean` for the Tauri app
- `update` runs npm/cargo update checks
- `build-daisyui` builds a local `Feliz.DaisyUI` checkout if present

`build.bat` also publishes the helper automatically when its executable is missing.
The helper checks embedded hashes of its sources, project files, and build configuration
at startup. If those inputs have changed, the batch file rebuilds it and retries the
original command once. Self-publishing requires the Windows Native AOT build tools
(Visual Studio C++ build tools and Windows SDK).

After cloning, run `.\build.bat build` with the prerequisites above installed.
The command restores npm dependencies, including the build tools, before starting
Tauri. The frontend build then restores the local .NET tools and Paket dependencies.
If npm restore fails, the build stops before starting Tauri.

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

Run the build helper's input-hash regression checks with:

```powershell
dotnet fsi --exec build/scripts/SelfBuild.Tests.fsx
```

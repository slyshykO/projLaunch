# Development, Build and Maintenance

[Documentation index](README.md) · [Project overview](../README.md) · [Development entry point](../AGENTS.md)

Run commands from the repository root. Source paths below are relative to that root.

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
npm ci
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
  Add `--release` for optimized output. See the [Git server guide](../crates/git-server/README.md)
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

### Portable Distribution

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

### Version Synchronization

Versions come from the nearest reachable release tag (`vX.Y.Z` or `X.Y.Z`),
using `git describe --tags`. The version is synchronized into every tracked
`package.json`, `tauri.conf.json`, explicit Cargo package/workspace version, and
the matching local-package entries in npm/Cargo lockfiles. Rust workspace members
keep `version.workspace = true`; dependency versions are preserved.

The build helper synchronizes versions for `build`, `run` and `build-git-server`
before building when tracked files or the index have uncommitted
changes, without committing those changes or tagging. On a clean checkout, if the
committed project versions still contain a generated `.dirty` suffix, the helper
removes only that suffix from manifests and lockfiles and creates one local commit:
`Remove dirty markers from project versions`. It preserves the existing version,
distance and hash; it does not create a tag or push. Once the markers are removed,
clean builds leave the committed files unchanged. This prevents a
commit/build/version-update cycle. Untracked files alone
do not trigger synchronization, matching the tracked-change scope of Git describe.
The npm `prebuild` path reaches synchronization through `build:git-server`.
`npm run gen:version` (also used by `pretauri`) runs
`build/scripts/version/version.fsx` to generate the display version; that script
currently does not synchronize tracked manifests. Use a standard build entry
point when manifest synchronization is required. Generated `version.fs` and
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

### Build Helper Lifecycle

`build.bat` also publishes the helper automatically when its executable is missing.
The helper checks embedded hashes of its sources, project files, and build configuration
at startup. If those inputs have changed, the batch file rebuilds it and retries the
original command once. Self-publishing requires the Windows Native AOT build tools
(Visual Studio C++ build tools and Windows SDK).

### Cleaning Artifacts

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

### First Build and Output Paths

After cloning, run `.\build.bat build` with the prerequisites above installed.
The command restores npm dependencies, including the build tools, before starting
Tauri. The frontend build then restores the local .NET tools and Paket dependencies.
If npm restore fails, the build stops before starting Tauri.

The app executable is written to `src-tauri/target/debug/projlaunch.exe` by default,
or `src-tauri/target/release/projlaunch.exe` with `--release`.

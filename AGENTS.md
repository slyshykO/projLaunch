# Repository Guidelines

## Start Here: Plans and Project Context

`AGENTS.md` is the main entry point for project work and all plans. Follow the current user request, then use these documents according to their roles:

- [GIT-GUI-PLAN.md](blueprints/GIT-GUI-PLAN.md): the current plan — developing the Git GUI into a client for everyday repository work. Keep its scope, steps, progress, implementation decisions, completed results, and validation in this same file. Proposed steps are not automatically approved tasks.
- [BACKLOG.md](blueprints/BACKLOG.md): distant-future ideas and deferred work, plus links to the separate plans created from them. It is not an active task tracker or implementation log.
- [CODEX-HANDOFF.md](blueprints/CODEX-HANDOFF.md): a frozen historical reference kept in the repository. Do not edit, append to, rename, or remove it. Existing instructions inside it to update the handoff or choose a next stage are historical and no longer apply. Its original relative paths and links refer to the repository root.

Keep plans and the backlog in `blueprints/`. When taking an item from the backlog, create a dedicated implementation plan in a separate file there and leave a link to that plan on the backlog entry. Keep the link after completion; do not delete the entry or duplicate execution details in the backlog. Update this entry point when a new plan becomes active.

Every plan owns its implementation record. When a step is implemented, mark it completed in that plan and document what changed, the decisions and their rationale, validation results, and any remaining limitations there. Keep completed steps and their notes in the same plan file. This applies to `blueprints/GIT-GUI-PLAN.md` and all future plans; do not transfer new implementation history into `blueprints/CODEX-HANDOFF.md`.

Source code and manifest/lock files are the authority for current behavior and dependencies; historical notes may describe earlier states. Preserve the user's local changes and check `git status` before editing.

## Project Structure & Module Organization

ProjLaunch is a desktop project launcher using F#/Fable, Elmish/Feliz, and Rust/Tauri.

- `src/`: frontend project, HTML/CSS entry point and `assets/`.
- `src/src/`: F# entry point and state; `common/`, `interop/`, `tauri/`, `domain/` and `ui/` group supporting code.
- `src-tauri/`: native commands, SQLite storage, `migrations/`, and desktop icons.
- `crates/`: separate Cargo workspace containing Git protocol, client, and server.
- `src-test/`: xUnit tests; `scripts/tests/`: frontend regression scripts and serialization probes.
- `build/`: F# build helper and regression scripts.
- `blueprints/`: implementation plans, future backlog, and the frozen historical handoff.
- `docs/`: user guides, development/build instructions, testing, and architecture reference.

Keep [README.md](README.md) limited to a short project summary and user-facing feature list. Detailed usage and technical documentation belong in `docs/`:

- [Documentation index](docs/README.md)
- [Development and builds](docs/DEVELOPMENT.md)
- [Testing](docs/TESTING.md)
- [Architecture and storage](docs/ARCHITECTURE.md)
- [User guide](docs/USER-GUIDE.md) and [remote repositories](docs/REMOTE-REPOSITORIES.md)

The detailed [Git server API guide](crates/git-server/README.md) remains next to the server code. Documentation in `docs/` describes implemented behavior; plans and implementation records stay in `blueprints/`.

## Architecture to Preserve

- Keep the launcher and opening projects in IDEs available as the Git GUI evolves.
- Use `AppTauri.Git`; keep stdio, server processes, and transport management out of the frontend UI.
- Preserve stale-response guards based on repository, selection, and request generation.
- After reconnecting, reopen repositories and discard old runtime repo IDs and history cursors.
- Preserve explicit merge-parent selection for diff and binary/truncated/empty states.
- Use Lucide through `src/src/ui/Icons.fs` for UI icons. Octicons are reserved for Git-specific gaps; add them only when needed. Keep brand logos separate. See [UI icon conventions](docs/ARCHITECTURE.md#ui-icons).

## Build, Test, and Development Commands

Run commands from the repository root. Install Node/npm, Git, Rust, the SDK selected by `global.json`, .NET 10 runtime, and Windows/Tauri prerequisites.

- `npm ci`, `dotnet tool restore`, `dotnet paket restore`: restore dependencies.
- `npm run tauri dev`: start desktop development; `npm run dev`: frontend watch mode.
- `npm run build`: compile frontend into `dist/` and prepare the release Git sidecar.
- `.\build.bat build`: build the debug desktop app; append `--release` for release.
- `.\build.bat run`: build and launch the app.

Before committing implementation changes, prefer a standard build entry point that runs version synchronization: `.\build.bat build` for the desktop app or `npm run build` for the frontend. If using direct compiler commands for targeted validation, run `npm run gen:version` first so the build includes current Git version metadata. Do not bypass version hooks just to keep manifests unchanged.

Version hooks may edit tracked manifests and lockfiles or commit cleanup of stale `.dirty` suffixes; review `git status` afterward and include all resulting project-version changes in the requested commit.

Keep `.cargo/config.toml` local and ignored. It contains machine-specific build workarounds; do not force-add it to Git or apply those settings to other PCs.

## Coding Style & Naming Conventions

Match surrounding formatting, generally four-space indentation in F#, Rust, and test JavaScript. Use PascalCase F# types/union cases and camelCase values; Rust functions/modules use snake_case. Preserve dependency order in `src/app.fsproj` when adding F# files. Respect `.editorconfig`'s Elmish setting. Git workspace Clippy checks are documented in its README.

After editing source files, always format F# with Fantomas and Rust with `cargo fmt`.

## Testing Guidelines

- `dotnet test src-test/src-test.fsproj`: xUnit suite, currently a placeholder.
- `npm run build:git-server`, then `cargo test --manifest-path src-tauri/Cargo.toml --lib`: native tests.
- `cargo test --manifest-path crates/Cargo.toml --workspace --all-features --locked`: Git workspace tests.
- Compile with `dotnet fable src -o ./src/bin/fable-js --configuration Debug`, then run `node scripts/tests/test-project-editor.mjs` and related `test-*.mjs` checks.

Use descriptive behavior names, Rust `snake_case` tests, temporary directories, and isolated IPC stubs. No coverage threshold is configured.

## Commit & Pull Request Guidelines

History favors short imperative subjects, such as `Add test scripts for Serde probes`; some commits use `feat:`. Keep commits focused. PRs should describe behavior changes, list validation commands/results, link relevant issues, and include screenshots for UI changes. Update the relevant plan with implementation decisions, completion status, and validation; update the appropriate `docs/` guide when behavior changes and the short README feature list when needed. Leave the frozen `blueprints/CODEX-HANDOFF.md` unchanged. Exclude generated build outputs and personal app data.

Always commit project-version changes in tracked manifests and lockfiles together with the work being committed, including version changes already present before the task. This includes `package.json`, `package-lock.json`, `src-tauri/tauri.conf.json`, Cargo manifests and Cargo lockfiles. These changes are part of the build workflow: do not revert, exclude or leave them uncommitted merely because they were generated automatically. Review mixed diffs so unrelated non-version edits are not included accidentally.

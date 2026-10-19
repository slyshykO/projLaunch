# Repository Guidelines

## Project Structure & Module Organization

ProjLaunch is a desktop project launcher using F#/Fable, Elmish/Feliz, and Rust/Tauri.

- `src/`: frontend project, HTML/CSS entry point and `assets/`.
- `src/src/`: F# entry point and state; `common/`, `interop/`, `tauri/`, `domain/` and `ui/` group supporting code.
- `src-tauri/`: native commands, SQLite storage, `migrations/`, and desktop icons.
- `crates/`: separate Cargo workspace containing Git protocol, client, and server.
- `src-test/`: xUnit tests; `scripts/tests/`: frontend regression scripts and serialization probes.
- `build/`: F# build helper and regression scripts.

Read `README.md` for setup, `CODEX-HANDOFF.md` for development context, and `crates/git-server/README.md` for the Git API.

## Build, Test, and Development Commands

Run commands from the repository root. Install Node/npm, Git, Rust, the SDK selected by `global.json`, .NET 10 runtime, and Windows/Tauri prerequisites.

- `npm ci`, `dotnet tool restore`, `dotnet paket restore`: restore dependencies.
- `npm run tauri dev`: start desktop development; `npm run dev`: frontend watch mode.
- `npm run build`: compile frontend into `dist/` and prepare the release Git sidecar.
- `.\build.bat build`: build the debug desktop app; append `--release` for release.
- `.\build.bat run`: build and launch the app.

Version hooks may edit manifests or commit cleanup of stale `.dirty` suffixes; review `git status` afterward.

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

History favors short imperative subjects, such as `Add test scripts for Serde probes`; some commits use `feat:`. Keep commits focused. PRs should describe behavior changes, list validation commands/results, link relevant issues, and include screenshots for UI changes. Update README/handoff documentation when behavior changes. Exclude generated outputs and personal app data.

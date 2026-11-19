# Testing

[Documentation index](README.md) · [Project overview](../README.md) · [Development entry point](../AGENTS.md)

Run commands from the repository root. Source paths below are relative to that root.

## F# Test Project

Run tests with:

```powershell
dotnet test .\src-test\src-test.fsproj
```

The xUnit project currently contains a placeholder test. Frontend behavior is
covered by the compiled Fable regression scripts below.

## Native Application Tests

Build the Git sidecar before running the native tests, which include real Git
server fixtures as well as storage and logging:

```powershell
npm run build:git-server
cargo test --manifest-path src-tauri/Cargo.toml --lib --locked
```

These use temporary directories, never the real app-data directory. They cover
first-run import, both remote formats, preservation of source files, restart and
retry behavior, transactional rollback, duplicate IDs, database conflicts,
concurrent startup, asynchronous worker commands, workspace persistence,
write ownership and project deletion without modifying files or recreating cards.

## Git Workspace Tests

```powershell
cargo test --manifest-path crates/Cargo.toml --workspace --all-features --locked
cargo clippy --manifest-path crates/Cargo.toml --workspace --all-targets --all-features --locked -- -D warnings
```

For opt-in WSL/SSH integration scenarios, fixtures and platform requirements,
see the [Git server guide](../crates/git-server/README.md).

## Build Helper Checks

Run the build helper's regression checks with:

```powershell
dotnet fsi --exec build/scripts/SelfBuild.Tests.fsx
dotnet fsi --exec build/scripts/Versioning.Tests.fsx
dotnet fsi --exec build/scripts/GitServerProcess.Tests.fsx
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/tests/test-clean.ps1
```

The versioning checks create an isolated repository and a local bare remote. They
cover release guards (`main`, clean index/worktree, untracked and version-only
changes), commit/tag publication, and recovery from branch or tag push failures.
They never publish to the project's remote. Commit-hook fixtures are executable
on Unix as well as Windows.

## Frontend Regression Scripts

Generate the current display version and compile the frontend before running
the state, IPC and rendering checks:

```powershell
npm run gen:version
dotnet fable src -o ./src/bin/fable-js --configuration Debug
node scripts/tests/test-project-editor.mjs
node scripts/tests/test-project-deletion.mjs
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
node scripts/tests/test-create-commit.mjs
node scripts/tests/test-generate-commit-message.mjs
node scripts/tests/test-working-tree.mjs
node scripts/tests/test-index-changes.mjs
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
Deletion checks cover confirmation/cancel, errors and retry, late responses and
launches, shared tabs/subscriptions and coordination with window closing.
The Git checks cover refresh races, stale responses, errors/recovery and rendered
indicators. Rust tests also exercise workspace/solution discovery and real stdio
status checks; build the Git sidecar first with `npm run build:git-server`.
Index-change checks cover single/group selection, disabled controls, local/WSL/SSH
routing, watcher/read races, refresh after success/error, diff migration and
closing/reopening a tab during a write. Real Git fixtures verify index, worktree
and HEAD contents for partial changes, deletions, new/binary/literal files,
unborn HEAD, renames, conflicts, invalid/stale selections and external index locks.
The opt-in WSL integration test also stages/unstages a Unicode path and verifies
that its working contents remain intact.
Commit-creation checks cover message validation, all-index scope, local/WSL/SSH
routing, shared write exclusion, stale diffs, history/branch refresh, retained
drafts and errors across tab closure. Real Git/RPC tests verify exact commit trees,
parents and messages, unborn/detached HEAD, partial/untracked changes, stale HEAD
and branch, operation markers, index locks, identity, hooks and signing failures.
The runner also tests stderr preservation when Git exits before reading stdin.
AI generation tests run fake Codex/Claude executables through real Git RPC and
verify staged-only/all-change input, binary summaries, unchanged index/HEAD/worktree,
agent failures, invalid output, concurrent repository changes and cancellation.
No model account or live service is used.
Agent fixtures emit over 64 KiB of stderr on both success and failure, verifying
continued execution, bounded diagnostic tails and preserved final error text.
Runner tests retain timeout/cancellation and strict Git stderr-limit coverage.
Frontend tests cover project/endpoint selection, editable results, cancelled opens,
stale responses, and settings changes;
storage tests cover schema v3→v4 migration and agent persistence across restarts.
If the environment places a `.git` entry directly in `/tmp`, use a separate
temporary root, for example `TMPDIR=/var/tmp`, for Rust tests; discovery fixtures
otherwise see that ancestor as an extra repository.
History checks cover paging, duplicate requests, independent tabs, late replies
after closing a tab, empty repositories, failed reads and session recovery.
Commit file checks cover selection races, initial/merge commits, parent changes,
renames, escaped paths, failed reads/retry and selection across tabs/history refresh.
Diff checks cover exact paths/targets, selection races, close/reopen during loading,
retry, binary/empty/truncated patches, escaping and stale responses after tab closure.
Logging checks cover automatic runtime delivery, immediate errors/warnings,
bounded batches, Unicode/errors, transport failure recovery and draining before
native close. Rust logging tests write only to temporary directories.

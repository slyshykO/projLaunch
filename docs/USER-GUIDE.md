# User Guide

[Documentation index](README.md) · [Project overview](../README.md) · [Development entry point](../AGENTS.md)

## What The App Does

The app currently lets you:

- view saved projects in a grid sorted by `lastOpened`
- add a project from the UI with name, description, IDE, and solution/workspace path
- edit a project using the pencil button at the top right of its card, including SSH/WSL and environment variables
- remove a project using the trash button beside the pencil, with confirmation; files, Git repositories and open repository tabs are preserved
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
commit date, short hash and branch/tag labels. History loads in pages of 100.
The list-with-plus icon (`Load more commits`) appends the next page. The circular
arrows icon (`Refresh history`), the View menu refresh action and
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
Refresh and window focus reload the status and selected working-tree file groups.
Automatic refresh retains the open diff when its file still exists; explicit
Refresh changes closes it. Committed selections and their diffs remain unchanged.
Clean repositories omit the extra graph row; status failures are displayed
separately from history errors.

Use **Stage** on an Unstaged or Untracked row, or **Unstage** on a Staged row.
Checkboxes select files independently in each group; **Stage selected** /
**Unstage selected** applies to that group's checked files. **Stage all** /
**Unstage all** applies to the displayed group only. Clicking the filename still
opens its diff. These actions work through the selected local, WSL or SSH endpoint.

Operations affect whole files: staging a partially staged file replaces its
index contents with the current working version. Unstage resets its index entry
to HEAD (or removes it before the first commit), preserving working files and
HEAD. Renames include both paths. Staging a conflict file marks it resolved;
the panel explicitly indicates this effect. There is no hunk/line staging yet.

While an operation runs, index controls and refresh are disabled for that
repository. Watcher invalidations are deferred. Afterwards, status and files are
reloaded and an open diff follows the file to its new group when possible.
Loaded history pages and scroll position are preserved. Errors remain visible;
the app refreshes state but never automatically retries the write after a lost
connection or timeout. Check the refreshed changes before retrying.

Stale selections, index locks and directory selections produce errors rather
than silently including other files. In particular, replacing a file with a
directory can require handling that transition outside this whole-file UI.
Submodule staging updates its recorded commit only, not files inside it.

When a project is opened, the app updates only its `lastOpened` value. A late
launch completion cannot recreate a deleted project or overwrite edited fields.

Removing a project deletes its catalog entry from SQLite after confirmation.
Cancel or Escape keeps it; an error leaves the card and dialog available for
retry. Local, WSL and SSH entries can be removed without accessing their paths
or connecting to their servers. Shared repository tabs and subscriptions stay
active while another card or open tab uses them. Read-only instances cannot
remove projects. Existing legacy JSON files are preserved and are not reimported
after deletion. The database schema and workspace JSON format are unchanged.

Git indicators refresh after loading or saving projects and when the app regains
focus. For a directory or ordinary project file, the containing repository is used.
For `.code-workspace`, `.sln` and `.slnx`, explicitly listed folders/projects are
also checked, with duplicate worktree roots shown once. Child directories are not
scanned. Hover over an indicator for its full path, branch or error details.
Paths in workspace/solution files resolve relative to those files; the card's own
path must be absolute. Untracked, staged and unstaged files count as changes.
Failed checks are shown as unavailable, never as clean.

## Current Limitations

- Git supports viewing, observing and whole-file stage/unstage. Commits, branch changes, remote synchronization and partial staging are not implemented yet.
- Remote launch options are only wired up for VS Code.
- Remote Git supports x86_64 Linux with glibc (Ubuntu 22.04 or compatible); remote Windows, macOS and ARM64 binaries are not supported.
- Visual Studio launch paths are currently hard-coded for Windows installs.

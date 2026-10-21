module Workspace

/// Only durable UI choices. Transport IDs, cursors, patches and pending requests
/// always belong to the current process and are never saved.
type File = { target: string; path: string }

type Selection =
    {
        commit: GitProtocol.Commit option
        parent: int option
        file: File option
    }

type Tab =
    {
        root: string
        name: string
        filesWidth: float
        filesHeight: float
        selection: Selection option
    }

type Snapshot =
    {
        version: int
        tabs: Tab array
        activeRoot: string option
    }

let targetName =
    function
    | GitProtocol.Commit _ -> "commit"
    | GitProtocol.Staged -> "staged"
    | GitProtocol.Unstaged -> "unstaged"
    | GitProtocol.Untracked -> "untracked"

let fileTarget (selection: Selection) (file: File) =
    match file.target, selection.commit with
    | "commit", Some commit -> Some(GitProtocol.Commit(commit.oid, selection.parent))
    | "staged", None -> Some GitProtocol.Staged
    | "unstaged", None -> Some GitProtocol.Unstaged
    | "untracked", None -> Some GitProtocol.Untracked
    | _ -> None

let panelSize value =
    if System.Double.IsNaN value || System.Double.IsInfinity value then
        40.0
    else
        max 20.0 (min 70.0 value)

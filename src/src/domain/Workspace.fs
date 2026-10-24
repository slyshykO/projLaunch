module Workspace

[<Literal>]
let CurrentVersion = 3

/// Only durable UI choices. Transport IDs, cursors, patches and pending requests
/// always belong to the current process and are never saved.
type File = { target: string; path: string }

type Selection =
    {
        commit: GitProtocol.Commit option
        parent: int option
        file: File option
    }

type Scroll =
    {
        top: float
        left: float
        anchor: string option
        offset: float
    }

type History = { count: int; scroll: Scroll }

let emptyScroll =
    {
        top = 0.0
        left = 0.0
        anchor = None
        offset = 0.0
    }

let emptyHistory = { count = 0; scroll = emptyScroll }

type Tab =
    {
        endpoint: GitProtocol.Endpoint option
        root: string
        name: string
        filesWidth: float
        filesHeight: float
        selection: Selection option
        history: History option
    }

type Snapshot =
    {
        version: int
        tabs: Tab array
        activeRoot: string option
        activeEndpoint: GitProtocol.Endpoint option
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

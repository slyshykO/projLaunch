module AppTauri

open Fable.Core
open Fable.Core.JsInterop

module Core =

    let isAvailable () = Tauri.Tauri.Tauri.isTauri ()

    let invoke<'T> (command: string) (args: obj option) : JS.Promise<'T> =
        promise {
            try
                return! Tauri.Tauri.Tauri.invoke (command, args)
            with error ->
                // Do not log args: project records may contain environment secrets.
                FrontendLog.error $"IPC {command} failed: {error}"
                return raise error
        }

    let greet (name: string) : JS.Promise<string> =
        invoke "greet" (Some(createObj [ "name" ==> name ]))

    let getDataDir () : JS.Promise<string> = Tauri.Tauri.Tauri.appDataDir ()

    let getConfigDir () : JS.Promise<string> = Tauri.Tauri.Tauri.appConfigDir ()

    let getAppVersion () : JS.Promise<string> = Tauri.Tauri.Tauri.getVersion ()

    let getTauriVersion () : JS.Promise<string> = Tauri.Tauri.Tauri.getTauriVersion ()

module Storage =
    type Access =
        { canWrite: bool; ownerPid: int option }

    let access () : JS.Promise<Access> = Core.invoke "get_storage_access" None

module WorkspaceState =
    let load () : JS.Promise<Workspace.Snapshot option> = Core.invoke "load_workspace" None

    let save (workspace: Workspace.Snapshot) : JS.Promise<unit> =
        Core.invoke "save_workspace" (Some(createObj [ "workspace" ==> workspace ]))

module Projects =

    let getProjects () : JS.Promise<string array> = Core.invoke "get_projects" None

    /// The delay is frontend-only and is useful for app startup sequencing.
    let getProjectsAfterStartup (startupDelayMs: int) : JS.Promise<string array> =
        promise {
            if startupDelayMs > 0 then
                do! Promise.sleep startupDelayMs

            return! getProjects ()
        }

    let openProject (id: string) : JS.Promise<unit> =
        Core.invoke "open_project" (Some(createObj [ "id" ==> id ]))

    /// The legacy IPC command name is retained; the record is saved in SQLite.
    let saveProject (id: string) (content: string) : JS.Promise<unit> =
        Core.invoke "rewrite_project_file" (Some(createObj [ "id" ==> id; "content" ==> content ]))

/// Transport and process lifetime stay in Rust. Operation IDs are caller-owned
/// so an in-flight read can be cancelled without knowing its JSON-RPC request ID.
module Git =
    let projectStatus (id: string) : JS.Promise<GitProtocol.ProjectStatus> =
        Core.invoke "git_project_status" (Some(createObj [ "id" ==> id ]))

    let private invoke<'T> endpoint command fields : JS.Promise<'T> =
        Core.invoke command (Some(createObj [ "params" ==> createObj fields; "endpoint" ==> endpoint ]))

    let watch endpoint path subscriptionId : JS.Promise<bool> =
        invoke endpoint "git_watch" [ "path" ==> path; "subscriptionId" ==> subscriptionId ]

    let unwatch endpoint subscriptionId : JS.Promise<bool> =
        invoke endpoint "git_unwatch" [ "subscriptionId" ==> subscriptionId ]

    let onChanged (handler: GitProtocol.ChangeEvent -> unit) : JS.Promise<Tauri.Event.UnlistenFn> =
        Tauri.Window.Window
            .getCurrent()
            .listen(
                U2.Case1 "git-repository-changed",
                fun (event: Tauri.Event.Event<GitProtocol.ChangeEvent>) -> handler event.payload
            )

    let private target =
        function
        | GitProtocol.Unstaged -> createObj [ "kind" ==> "unstaged" ]
        | GitProtocol.Staged -> createObj [ "kind" ==> "staged" ]
        | GitProtocol.Untracked -> createObj [ "kind" ==> "untracked" ]
        | GitProtocol.Commit(oid, parent) -> createObj [ "kind" ==> "commit"; "oid" ==> oid; "parent" ==> parent ]

    let hello (endpoint: GitProtocol.Endpoint option) : JS.Promise<GitProtocol.Hello> =
        Core.invoke "git_hello" (Some(createObj [ "endpoint" ==> endpoint ]))

    let openRepository
        (endpoint: GitProtocol.Endpoint option)
        (path: string)
        (operationId: string)
        : JS.Promise<GitProtocol.Repository> =
        invoke endpoint "git_open" [ "path" ==> path; "operationId" ==> operationId ]

    let status
        (endpoint: GitProtocol.Endpoint option)
        (repoId: string)
        (operationId: string)
        : JS.Promise<GitProtocol.Status> =
        invoke endpoint "git_status" [ "repoId" ==> repoId; "operationId" ==> operationId ]

    let branches
        (endpoint: GitProtocol.Endpoint option)
        (repoId: string)
        (operationId: string)
        : JS.Promise<GitProtocol.BranchList> =
        invoke endpoint "git_branches" [ "repoId" ==> repoId; "operationId" ==> operationId ]

    let log
        (endpoint: GitProtocol.Endpoint option)
        (repoId: string)
        (operationId: string)
        (cursor: string option)
        (limit: int)
        : JS.Promise<GitProtocol.LogPage> =
        invoke
            endpoint
            "git_log"
            [
                "repoId" ==> repoId
                "operationId" ==> operationId
                "cursor" ==> cursor
                "limit" ==> limit
            ]

    let diffFiles
        (endpoint: GitProtocol.Endpoint option)
        (repoId: string)
        (operationId: string)
        diffTarget
        : JS.Promise<GitProtocol.DiffFile array> =
        invoke
            endpoint
            "git_diff_files"
            [
                "repoId" ==> repoId
                "operationId" ==> operationId
                "target" ==> target diffTarget
            ]

    let diff
        (endpoint: GitProtocol.Endpoint option)
        (repoId: string)
        (operationId: string)
        diffTarget
        (path: string)
        : JS.Promise<GitProtocol.Patch> =
        invoke
            endpoint
            "git_diff"
            [
                "repoId" ==> repoId
                "operationId" ==> operationId
                "target" ==> target diffTarget
                "path" ==> path
            ]

    let cancel (endpoint: GitProtocol.Endpoint option) (operationId: string) : JS.Promise<bool> =
        invoke endpoint "git_cancel" [ "operationId" ==> operationId ]

    /// Invalidates all repoIds and history cursors belonging to this connection.
    let disconnect () : JS.Promise<unit> = Core.invoke "git_disconnect" None

module Window =

    let current () = Tauri.Window.Window.getCurrent ()

    let saveState () : JS.Promise<unit> =
        Tauri.WindowState.saveWindowState Tauri.WindowState.StateFlags.ALL

    let closeCurrent () : JS.Promise<unit> = current().close()

    let onAddProject (handler: unit -> unit) : JS.Promise<Tauri.Event.UnlistenFn> =
        current().listen(U2.Case1 "menu-add-project", fun (_: Tauri.Event.Event<unit>) -> handler ())

    let onMenuAction (handler: string -> unit) : JS.Promise<Tauri.Event.UnlistenFn> =
        current().listen(U2.Case1 "menu-action", fun (event: Tauri.Event.Event<string>) -> handler event.payload)

    let onResized (handler: Tauri.Dpi.PhysicalSize -> unit) : JS.Promise<Tauri.Event.UnlistenFn> =
        current().onResized(fun e -> handler e.payload)

    let onMoved (handler: Tauri.Dpi.PhysicalPosition -> unit) : JS.Promise<Tauri.Event.UnlistenFn> =
        current().onMoved(fun e -> handler e.payload)

    let onCloseRequested (handler: Tauri.Window.CloseRequestedEvent -> unit) : JS.Promise<Tauri.Event.UnlistenFn> =
        current().onCloseRequested handler

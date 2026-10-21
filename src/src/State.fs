module state

open Elmish
open System
open projectData


type SignIn = { nick: string; role: string }

type ProjectGitCheck =
    {
        requestId: string
        path: string
        remote: Remote option
        isLoading: bool
        result: GitProtocol.ProjectStatus option
        error: string option
    }

type RepositoryHistory =
    {
        repoId: string option
        commits: GitProtocol.Commit array
        nextCursor: string option
        requestId: string option
        hasLoaded: bool
        error: string option
    }

type CommitDiff =
    {
        file: GitProtocol.DiffFile
        target: GitProtocol.DiffTarget
        requestId: string option
        patch: GitProtocol.Patch option
        error: string option
    }

type CommitFiles =
    {
        commit: GitProtocol.Commit option
        groups: (GitProtocol.DiffTarget * GitProtocol.DiffFile array) array
        parent: int option
        files: GitProtocol.DiffFile array
        requestId: string option
        hasLoaded: bool
        error: string option
        diff: CommitDiff option
    }

type RepositoryTab =
    {
        id: string
        root: string
        name: string
        filesWidth: float
        filesHeight: float
        restoreFile: Workspace.File option
        parentNavigation: string option
        parentNavigationError: string option
        history: RepositoryHistory
        workingTree: Result<GitProtocol.Status, string> option
        selection: CommitFiles option
    }

type Msg =
    | Empty

    | Start
    | OnStart of unit

    | LoadWorkspace
    | OnStorageAccess of Result<AppTauri.Storage.Access, exn>
    | OnWorkspaceLoaded of int * Result<Workspace.Snapshot option, exn>
    | SaveWorkspace
    | OnWorkspaceSaved of int * Result<unit, exn>
    | ContinueWindowClose
    | SaveWindowAndClose
    | ResizeRepositoryPanels of string * bool * float
    | ShowProjects
    | OpenRepositoryTab of GitProtocol.RepositoryStatus
    | SelectRepositoryTab of string
    | CloseRepositoryTab of string
    | CloseActiveTab
    | RefreshRepositoryHistory of string
    | LoadMoreCommits of string
    | OnRepositoryHistory of string * string * bool * Result<string * GitProtocol.LogPage, exn>
    | OnRepositoryRefresh of
        string *
        string *
        Result<string * GitProtocol.LogPage, exn> *
        Result<GitProtocol.Status, string>
    | SelectWorkingTree of string
    | SelectWorkingTreeFile of string * GitProtocol.DiffTarget * string
    | SelectRepositoryCommit of tabId: string * oid: string
    | NavigateToParent of tabId: string * oid: string
    | ContinueParentNavigation of tabId: string * oid: string
    | CancelParentNavigation of tabId: string
    | SelectCommitParent of tabId: string * parent: int
    | RefreshCommitFiles of tabId: string
    | OnCommitFiles of
        tabId: string *
        requestId: string *
        Result<(GitProtocol.DiffTarget * GitProtocol.DiffFile array) array, exn>
    | SelectCommitFile of tabId: string * path: string
    | RetryCommitDiff of tabId: string
    | CloseCommitDiff of tabId: string
    | OnCommitDiff of tabId: string * requestId: string * Result<GitProtocol.Patch, exn>
    | OpenAbout
    | CloseAbout

    | GetDataDir
    | OnGetDataDir of string
    | OnGetDataDirError of exn

    | GetConfigDir
    | OnGetConfigDir of string
    | OnGetConfigDirError of exn

    | GetAppVersion
    | OnAppVersion of string
    | OnAppVersionError of exn

    | GetTauriVersion
    | OnGetTauriVersion of string
    | OnGetTauriVersionError of exn

    | GetProjects of int
    | OnGetProjectsSuccess of string array
    | OnGetProjectsError of exn
    | RefreshGitStatuses
    | RefreshProjectGit of string
    | OnProjectGitStatus of string * string * Result<GitProtocol.ProjectStatus, exn>

    | OpenProject of string
    | OnOpenProjectSuccess of string
    | OnOpenProjectError of exn

    | AddOrUpdateProject of string * string
    | OnAddOrUpdateProjectSuccess of string
    | OnAddOrUpdateProjectError of exn
    | OpenAddProjectModal
    | CloseProjectEditor
    | OpenEditProjectModal of string
    | SaveProject of ProjectData
    | OnSaveProjectSuccess of ProjectData
    | OnSaveProjectError of exn

    | WindowSave
    | OnWindowSaveSuccess
    | OnWindowSaveError of exn
    | OnWindowCloseError of exn

    | WindowResized of Tauri.Dpi.PhysicalSize
    | WindowMoved of Tauri.Dpi.PhysicalPosition

    | Tick of DateTime


type State =
    {
        storageAccess: AppTauri.Storage.Access option
        workspaceReady: bool
        workspaceLoading: bool
        workspaceRevision: int
        workspaceSavedRevision: int
        workspaceSaving: int option
        workspaceError: string option
        repositoryTabs: RepositoryTab list
        activeRepositoryTab: string option
        isAboutOpen: bool
        currentTime: DateTime
        signIn: option<SignIn>
        appVersion: string
        tauriVersion: string
        appDataDir: string // C:\Users\alex\AppData\Roaming\com.projlaunch.app
        appConfigDir: string // C:\Users\alex\AppData\Roaming\com.projlaunch.app
        projects: ProjectData list
        projectStorageError: string option
        projectGit: Map<string, ProjectGitCheck>
        projectEditor: ProjectData option
        isProjectSaving: bool
        projectSaveError: string option
        randomSalt: string
        appWindowSize: Tauri.Dpi.PhysicalSize option
        appWindowPosition: Tauri.Dpi.PhysicalPosition option
        isWindowClosePending: bool
    }

let mutable allowWindowClose = false

let init () =

    let pd =
        if Debug.Debug && not (AppTauri.Core.isAvailable ()) then
            listOfProjectDataFromJsonStr Debug.projectsJsonStr
        else
            []

    // generate a random salt
    let randomSalt = Guid.NewGuid().ToString()

    // Initial state
    let state =
        {
            storageAccess = None
            workspaceReady = false
            workspaceLoading = false
            workspaceRevision = 0
            workspaceSavedRevision = 0
            workspaceSaving = None
            workspaceError = None
            repositoryTabs = []
            activeRepositoryTab = None
            isAboutOpen = false
            currentTime = DateTime.Now
            signIn = None
            appVersion = ""
            tauriVersion = ""
            appDataDir = ""
            appConfigDir = ""
            projects = pd
            projectStorageError = None
            projectGit = Map.empty
            projectEditor = None
            isProjectSaving = false
            projectSaveError = None
            randomSalt = randomSalt
            appWindowSize = None
            appWindowPosition = None
            isWindowClosePending = false
        }

    state, Cmd.ofMsg Start

let private newRepositoryTab root name =
    {
        id = Guid.NewGuid().ToString()
        root = root
        name = name
        filesWidth = 40.0
        filesHeight = 40.0
        restoreFile = None
        parentNavigation = None
        parentNavigationError = None
        selection = None
        workingTree = None
        history =
            {
                repoId = None
                commits = [||]
                nextCursor = None
                requestId = None
                hasLoaded = false
                error = None
            }
    }

let workspaceSnapshot (state: State) : Workspace.Snapshot =
    {
        version = 1
        activeRoot =
            state.repositoryTabs
            |> List.tryFind (fun tab -> Some tab.id = state.activeRepositoryTab)
            |> Option.map (fun tab -> tab.root)
        tabs =
            state.repositoryTabs
            |> List.map (fun tab ->
                ({
                    Workspace.Tab.root = tab.root
                    name = tab.name
                    filesWidth = tab.filesWidth
                    filesHeight = tab.filesHeight
                    selection =
                        tab.selection
                        |> Option.map (fun selection ->
                            {
                                Workspace.Selection.commit = selection.commit
                                parent = selection.parent
                                file =
                                    selection.diff
                                    |> Option.map (fun diff ->
                                        ({
                                            Workspace.File.target = Workspace.targetName diff.target
                                            path = diff.file.path
                                        }
                                        : Workspace.File))
                                    |> Option.orElse tab.restoreFile
                            })
                }
                : Workspace.Tab))
            |> List.toArray
    }

let private restoreTab (saved: Workspace.Tab) =
    { newRepositoryTab saved.root saved.name with
        filesWidth = Workspace.panelSize saved.filesWidth
        filesHeight = Workspace.panelSize saved.filesHeight
        restoreFile = saved.selection |> Option.bind (fun selection -> selection.file)
        selection =
            saved.selection
            |> Option.map (fun selection ->
                {
                    commit = selection.commit
                    parent = selection.parent
                    groups = [||]
                    files = [||]
                    requestId = None
                    hasLoaded = false
                    error = None
                    diff = None
                })
    }

let isStorageReadOnly state =
    state.storageAccess |> Option.exists (fun access -> not access.canWrite)

let rec private updateCore msg state =
    match msg with
    | OpenAddProjectModal
    | OpenEditProjectModal _
    | SaveProject _
    | AddOrUpdateProject _
    | OnOpenProjectSuccess _ when isStorageReadOnly state -> state, Cmd.none
    | Empty -> state, Cmd.none
    | Start ->
        let delayPromise () =
            promise {
                do! Promise.sleep 100
                return ()
            }

        state, Cmd.OfPromise.perform delayPromise () OnStart

    | OnStart() ->
        if AppTauri.Core.isAvailable () then
            state,
            Cmd.batch
                [
                    Cmd.ofMsg LoadWorkspace
                    Cmd.ofMsg (GetProjects 50)
                    Cmd.ofMsg GetConfigDir
                    Cmd.ofMsg GetDataDir
                    Cmd.ofMsg GetAppVersion
                    Cmd.ofMsg GetTauriVersion
                ]
        else
            state, Cmd.none

    | LoadWorkspace when
        state.storageAccess.IsNone
        && not state.workspaceLoading
        && AppTauri.Core.isAvailable ()
        ->
        { state with
            workspaceLoading = true
            workspaceError = None
        },
        Cmd.OfPromise.either AppTauri.Storage.access () (Ok >> OnStorageAccess) (Error >> OnStorageAccess)

    | OnStorageAccess result ->
        match result with
        | Ok access ->
            { state with
                storageAccess = Some access
                workspaceLoading = false
                projectEditor = if access.canWrite then state.projectEditor else None
            },
            Cmd.ofMsg LoadWorkspace
        | Error error ->
            { state with
                workspaceLoading = false
                workspaceError = Some $"Could not open database: {error}"
            },
            Cmd.ofMsg ContinueWindowClose

    | LoadWorkspace ->
        if
            state.workspaceLoading
            || state.workspaceReady
            || not (AppTauri.Core.isAvailable ())
        then
            state, Cmd.none
        else
            { state with
                workspaceLoading = true
                workspaceError = None
            },
            Cmd.OfPromise.either
                AppTauri.WorkspaceState.load
                ()
                (fun snapshot -> OnWorkspaceLoaded(state.workspaceRevision, Ok snapshot))
                (fun error -> OnWorkspaceLoaded(state.workspaceRevision, Error error))

    | OnWorkspaceLoaded(revision, result) ->
        if not state.workspaceLoading then
            state, Cmd.none
        else
            match result with
            | Error error ->
                { state with
                    workspaceLoading = false
                    workspaceError = Some $"Could not restore workspace: {error}"
                },
                Cmd.ofMsg ContinueWindowClose
            | Ok snapshot ->
                let restored =
                    snapshot
                    |> Option.map (fun saved -> saved.tabs |> Array.map restoreTab |> Array.toList)
                    |> Option.defaultValue []

                let changed = revision <> state.workspaceRevision

                let tabs =
                    if changed then
                        state.repositoryTabs
                        @ (restored
                           |> List.filter (fun tab ->
                               state.repositoryTabs |> List.forall (fun existing -> existing.root <> tab.root)))
                    else
                        restored

                let active =
                    if changed then
                        state.activeRepositoryTab
                    else
                        snapshot
                        |> Option.bind (fun saved -> saved.activeRoot)
                        |> Option.bind (fun root -> tabs |> List.tryFind (fun tab -> tab.root = root))
                        |> Option.map (fun tab -> tab.id)

                { state with
                    workspaceReady = true
                    workspaceLoading = false
                    workspaceError = None
                    repositoryTabs = tabs
                    activeRepositoryTab = active
                    workspaceSavedRevision =
                        if changed then
                            state.workspaceSavedRevision
                        else
                            state.workspaceRevision
                },
                Cmd.batch
                    [
                        if changed then
                            Cmd.ofMsg SaveWorkspace
                        match active with
                        | Some id -> Cmd.ofMsg (RefreshRepositoryHistory id)
                        | None -> ()
                        Cmd.ofMsg ContinueWindowClose
                    ]

    | SaveWorkspace ->
        if
            isStorageReadOnly state
            || not state.workspaceReady
            || state.workspaceSaving.IsSome
            || state.workspaceSavedRevision >= state.workspaceRevision
        then
            state, Cmd.none
        else
            let revision = state.workspaceRevision

            { state with
                workspaceSaving = Some revision
                workspaceError = None
            },
            Cmd.OfPromise.either
                AppTauri.WorkspaceState.save
                (workspaceSnapshot state)
                (fun () -> OnWorkspaceSaved(revision, Ok()))
                (fun error -> OnWorkspaceSaved(revision, Error error))

    | OnWorkspaceSaved(revision, result) ->
        if state.workspaceSaving <> Some revision then
            state, Cmd.none
        else
            match result with
            | Error error ->
                { state with
                    workspaceSaving = None
                    workspaceError = Some $"Could not save workspace: {error}"
                    isWindowClosePending = false
                },
                Cmd.none
            | Ok() ->
                { state with
                    workspaceSaving = None
                    workspaceSavedRevision = revision
                    workspaceError = None
                },
                Cmd.batch
                    [
                        if state.workspaceRevision > revision then
                            Cmd.ofMsg SaveWorkspace
                        Cmd.ofMsg ContinueWindowClose
                    ]

    | ResizeRepositoryPanels(id, horizontal, size) ->
        let tabs =
            state.repositoryTabs
            |> List.map (fun tab ->
                if tab.id <> id then
                    tab
                elif horizontal then
                    { tab with
                        filesWidth = Workspace.panelSize size
                    }
                else
                    { tab with
                        filesHeight = Workspace.panelSize size
                    })

        { state with repositoryTabs = tabs }, Cmd.none

    | ShowProjects ->
        { state with
            activeRepositoryTab = None
        },
        Cmd.none
    | OpenRepositoryTab repository ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.root = repository.root) with
        | Some tab ->
            { state with
                activeRepositoryTab = Some tab.id
            },
            if not tab.history.hasLoaded && tab.history.requestId.IsNone then
                Cmd.ofMsg (RefreshRepositoryHistory tab.id)
            else
                Cmd.none
        | None ->
            let tab = newRepositoryTab repository.root repository.name

            { state with
                repositoryTabs = state.repositoryTabs @ [ tab ]
                activeRepositoryTab = Some tab.id
            },
            Cmd.ofMsg (RefreshRepositoryHistory tab.id)
    | SelectRepositoryTab id ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab ->
            { state with
                activeRepositoryTab = Some id
            },
            if not tab.history.hasLoaded && tab.history.requestId.IsNone then
                Cmd.ofMsg (RefreshRepositoryHistory id)
            else
                Cmd.none
        | None -> state, Cmd.none
    | CloseActiveTab ->
        state,
        (state.activeRepositoryTab
         |> Option.map (CloseRepositoryTab >> Cmd.ofMsg)
         |> Option.defaultValue Cmd.none)
    | CloseRepositoryTab id ->
        let remaining = state.repositoryTabs |> List.filter (fun tab -> tab.id <> id)

        let active =
            if state.activeRepositoryTab <> Some id then
                state.activeRepositoryTab
            else
                state.repositoryTabs
                |> List.takeWhile (fun tab -> tab.id <> id)
                |> List.tryLast
                |> Option.map (fun tab -> tab.id)

        { state with
            repositoryTabs = remaining
            activeRepositoryTab = active
        },
        (if active <> state.activeRepositoryTab then
             active
             |> Option.map (SelectRepositoryTab >> Cmd.ofMsg)
             |> Option.defaultValue Cmd.none
         else
             Cmd.none)
    | OpenAbout ->
        if state.projectEditor.IsSome then
            state, Cmd.none
        else
            { state with isAboutOpen = true }, Cmd.none
    | CloseAbout -> { state with isAboutOpen = false }, Cmd.none

    | SelectWorkingTree id ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab when tab.history.requestId.IsNone ->
            let selection =
                {
                    commit = None
                    parent = None
                    groups = [||]
                    files = [||]
                    requestId = None
                    hasLoaded = false
                    error = None
                    diff = None
                }

            { state with
                repositoryTabs =
                    state.repositoryTabs
                    |> List.map (fun item ->
                        if item.id = id then
                            { item with
                                selection = Some selection
                                restoreFile = None
                            }
                        else
                            item)
            },
            Cmd.ofMsg (RefreshCommitFiles id)
        | _ -> state, Cmd.none

    | NavigateToParent(id, oid) ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab when
            tab.selection
            |> Option.bind (fun selection -> selection.commit)
            |> Option.exists (fun commit -> Array.contains oid commit.parents)
            ->
            let next =
                { state with
                    repositoryTabs =
                        state.repositoryTabs
                        |> List.map (fun item ->
                            if item.id = id then
                                { item with
                                    parentNavigation = Some oid
                                    parentNavigationError = None
                                }
                            else
                                item)
                }

            if tab.history.commits |> Array.exists (fun commit -> commit.oid = oid) then
                updateCore (ContinueParentNavigation(id, oid)) next
            elif tab.history.requestId.IsSome then
                next, Cmd.none
            elif
                tab.history.error.IsSome
                || not tab.history.hasLoaded
                || tab.history.nextCursor.IsNone
            then
                next, Cmd.ofMsg (RefreshRepositoryHistory id)
            else
                next, Cmd.ofMsg (ContinueParentNavigation(id, oid))
        | _ -> state, Cmd.none

    | CancelParentNavigation id ->
        { state with
            repositoryTabs =
                state.repositoryTabs
                |> List.map (fun tab ->
                    if tab.id = id then
                        { tab with
                            parentNavigation = None
                            parentNavigationError = None
                        }
                    else
                        tab)
        },
        Cmd.none

    | ContinueParentNavigation(id, oid) ->
        match
            state.repositoryTabs
            |> List.tryFind (fun tab -> tab.id = id && tab.parentNavigation = Some oid)
        with
        | Some tab ->
            if tab.history.commits |> Array.exists (fun commit -> commit.oid = oid) then
                updateCore (SelectRepositoryCommit(id, oid)) state
            elif tab.history.requestId.IsSome then
                state, Cmd.none
            elif tab.history.error.IsNone && tab.history.nextCursor.IsSome then
                state, Cmd.ofMsg (LoadMoreCommits id)
            else
                let error =
                    match tab.history.error with
                    | Some error -> $"Could not open parent {oid}. Click its hash to retry. {error}"
                    | None ->
                        $"Parent {oid} is not available in this repository's history. It may be missing from a shallow clone or no longer reachable."

                { state with
                    repositoryTabs =
                        state.repositoryTabs
                        |> List.map (fun item ->
                            if item.id = id then
                                { item with
                                    parentNavigation = None
                                    parentNavigationError = Some error
                                }
                            else
                                item)
                },
                Cmd.none
        | None -> state, Cmd.none

    | SelectRepositoryCommit(id, oid) ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab when
            tab.selection
            |> Option.exists (fun selection -> selection.commit |> Option.exists (fun commit -> commit.oid = oid))
            ->
            state, Cmd.none
        | Some tab ->
            match tab.history.commits |> Array.tryFind (fun commit -> commit.oid = oid) with
            | Some commit ->
                let selection =
                    {
                        commit = Some commit
                        groups = [||]
                        diff = None
                        parent = if commit.parents.Length > 0 then Some 1 else None
                        files = [||]
                        requestId = None
                        hasLoaded = false
                        error = None
                    }

                { state with
                    repositoryTabs =
                        state.repositoryTabs
                        |> List.map (fun item ->
                            if item.id = id then
                                { item with
                                    selection = Some selection
                                    restoreFile = None
                                    parentNavigation = None
                                    parentNavigationError = None
                                }
                            else
                                item)
                },
                Cmd.ofMsg (RefreshCommitFiles id)
            | None -> state, Cmd.none
        | None -> state, Cmd.none

    | SelectCommitParent(id, parent) ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab ->
            match tab.selection with
            | Some selection when
                parent >= 1
                && (selection.commit
                    |> Option.exists (fun commit -> parent <= commit.parents.Length))
                && selection.parent <> Some parent
                ->
                let next =
                    { selection with
                        parent = Some parent
                        diff = None
                        files = [||]
                        groups = [||]
                        requestId = None
                        hasLoaded = false
                        error = None
                    }

                { state with
                    repositoryTabs =
                        state.repositoryTabs
                        |> List.map (fun item ->
                            if item.id = id then
                                { item with
                                    selection = Some next
                                    restoreFile = None
                                }
                            else
                                item)
                },
                Cmd.ofMsg (RefreshCommitFiles id)
            | _ -> state, Cmd.none
        | None -> state, Cmd.none

    | RefreshCommitFiles id ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab ->
            match tab.selection with
            | Some selection when
                selection.requestId.IsNone
                && (selection.commit
                    |> Option.forall (fun commit -> commit.parents.Length <= 1 || selection.parent.IsSome))
                ->
                let requestId = Guid.NewGuid().ToString()

                let next =
                    { selection with
                        requestId = Some requestId
                        diff = None
                        files = [||]
                        groups = [||]
                        hasLoaded = false
                        error = None
                    }

                let read () =
                    promise {
                        // Reopen by root so retry also recovers from a restarted server.
                        let! repository = AppTauri.Git.openRepository tab.root $"files-{requestId}-open"

                        let targets =
                            match selection.commit with
                            | Some commit -> [| GitProtocol.Commit(commit.oid, selection.parent) |]
                            | None -> [| GitProtocol.Staged; GitProtocol.Unstaged; GitProtocol.Untracked |]

                        let groups = ResizeArray<_>()

                        for index, target in targets |> Array.indexed do
                            let! files =
                                AppTauri.Git.diffFiles repository.repoId $"files-{requestId}-{index}" target

                            groups.Add(target, files)

                        return groups.ToArray()
                    }

                { state with
                    repositoryTabs =
                        state.repositoryTabs
                        |> List.map (fun item ->
                            if item.id = id then
                                { item with selection = Some next }
                            else
                                item)
                },
                Cmd.OfPromise.either read () (fun files -> OnCommitFiles(id, requestId, Ok files)) (fun error ->
                    OnCommitFiles(id, requestId, Error error))
            | _ -> state, Cmd.none
        | None -> state, Cmd.none

    | OnCommitFiles(id, requestId, result) ->
        let mutable restore = Cmd.none

        let tabs =
            state.repositoryTabs
            |> List.map (fun tab ->
                match tab.selection with
                | Some selection when tab.id = id && selection.requestId = Some requestId ->
                    match result with
                    | Error error ->
                        { tab with
                            selection =
                                Some
                                    { selection with
                                        requestId = None
                                        error = Some(string error)
                                    }
                        }
                    | Ok groups ->
                        let pending =
                            tab.restoreFile
                            |> Option.bind (fun file ->
                                let saved: Workspace.Selection =
                                    {
                                        commit = selection.commit
                                        parent = selection.parent
                                        file = Some file
                                    }

                                Workspace.fileTarget saved file
                                |> Option.bind (fun target ->
                                    if
                                        groups
                                        |> Array.exists (fun (group, files) ->
                                            group = target
                                            && (files |> Array.exists (fun entry -> entry.path = file.path)))
                                    then
                                        restore <-
                                            Cmd.ofMsg (
                                                if selection.commit.IsSome then
                                                    SelectCommitFile(id, file.path)
                                                else
                                                    SelectWorkingTreeFile(id, target, file.path)
                                            )

                                        Some file
                                    else
                                        None))

                        { tab with
                            restoreFile = pending
                            selection =
                                Some
                                    { selection with
                                        groups = groups
                                        files = groups |> Array.collect snd |> Array.distinctBy (fun file -> file.path)
                                        requestId = None
                                        hasLoaded = true
                                        error = None
                                    }
                        }
                | _ -> tab)

        { state with repositoryTabs = tabs }, restore

    | SelectWorkingTreeFile(id, _, _)
    | SelectCommitFile(id, _)
    | RetryCommitDiff id ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab ->
            match tab.selection with
            | Some selection when selection.hasLoaded && selection.requestId.IsNone ->
                let requested =
                    match msg with
                    | SelectCommitFile(_, path) ->
                        selection.commit
                        |> Option.map (fun commit -> GitProtocol.Commit(commit.oid, selection.parent), path)
                    | SelectWorkingTreeFile(_, target, path) when selection.commit.IsNone -> Some(target, path)
                    | RetryCommitDiff _ ->
                        selection.diff
                        |> Option.filter (fun diff -> diff.requestId.IsNone)
                        |> Option.map (fun diff -> diff.target, diff.file.path)
                    | _ -> None

                let file =
                    requested
                    |> Option.bind (fun (target, path) ->
                        let alreadySelected =
                            match msg with
                            | RetryCommitDiff _ -> false
                            | _ ->
                                selection.diff
                                |> Option.exists (fun diff -> diff.target = target && diff.file.path = path)

                        if alreadySelected then
                            None
                        else
                            selection.groups
                            |> Array.tryFind (fun (group, _) -> group = target)
                            |> Option.bind (fun (_, files) -> files |> Array.tryFind (fun file -> file.path = path))
                            |> Option.map (fun file -> target, file))

                match file with
                | Some(target, file) ->
                    let requestId = Guid.NewGuid().ToString()

                    let diff =
                        {
                            file = file
                            target = target
                            requestId = Some requestId
                            patch = None
                            error = None
                        }

                    let read () =
                        promise {
                            let! repository = AppTauri.Git.openRepository tab.root $"diff-{requestId}-open"

                            return! AppTauri.Git.diff repository.repoId $"diff-{requestId}-patch" target file.path
                        }

                    { state with
                        repositoryTabs =
                            state.repositoryTabs
                            |> List.map (fun item ->
                                if item.id = id then
                                    { item with
                                        selection = Some { selection with diff = Some diff }
                                        restoreFile = None
                                    }
                                else
                                    item)
                    },
                    Cmd.OfPromise.either read () (fun patch -> OnCommitDiff(id, requestId, Ok patch)) (fun error ->
                        OnCommitDiff(id, requestId, Error error))
                | None -> state, Cmd.none
            | _ -> state, Cmd.none
        | None -> state, Cmd.none

    | CloseCommitDiff id ->
        { state with
            repositoryTabs =
                state.repositoryTabs
                |> List.map (fun tab ->
                    if tab.id = id then
                        { tab with
                            selection = tab.selection |> Option.map (fun selection -> { selection with diff = None })
                            restoreFile = None
                        }
                    else
                        tab)
        },
        Cmd.none

    | OnCommitDiff(id, requestId, result) ->
        { state with
            repositoryTabs =
                state.repositoryTabs
                |> List.map (fun tab ->
                    match tab.selection with
                    | Some selection when tab.id = id ->
                        match selection.diff with
                        | Some diff when diff.requestId = Some requestId ->
                            let next =
                                match result with
                                | Ok patch ->
                                    { diff with
                                        requestId = None
                                        patch = Some patch
                                        error = None
                                    }
                                | Error error ->
                                    { diff with
                                        requestId = None
                                        error = Some(string error)
                                    }

                            { tab with
                                selection = Some { selection with diff = Some next }
                            }
                        | _ -> tab
                    | _ -> tab)
        },
        Cmd.none

    | RefreshRepositoryHistory id
    | LoadMoreCommits id ->
        let append =
            match msg with
            | LoadMoreCommits _ -> true
            | _ -> false

        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab when
            tab.history.requestId.IsNone
            && (not append || (tab.history.repoId.IsSome && tab.history.nextCursor.IsSome))
            ->
            let requestId = Guid.NewGuid().ToString()

            let history =
                { tab.history with
                    requestId = Some requestId
                    error = None
                    repoId = if append then tab.history.repoId else None
                    nextCursor = if append then tab.history.nextCursor else None
                }

            let read () =
                promise {
                    let! repoId =
                        match append, tab.history.repoId with
                        | true, Some repoId -> promise { return repoId }
                        | _ ->
                            promise {
                                let! repository = AppTauri.Git.openRepository tab.root $"history-{requestId}-open"
                                return repository.repoId
                            }

                    let! result =
                        promise {
                            try
                                let! page =
                                    AppTauri.Git.log
                                        repoId
                                        $"history-{requestId}-log"
                                        (if append then tab.history.nextCursor else None)
                                        100

                                return Ok(repoId, page)
                            with error ->
                                return Error error
                        }

                    if append then
                        return OnRepositoryHistory(id, requestId, true, result)
                    else
                        let! status =
                            promise {
                                try
                                    let! status = AppTauri.Git.status repoId $"history-{requestId}-status"
                                    return Ok status
                                with error ->
                                    return Error(string error)
                            }

                        return OnRepositoryRefresh(id, requestId, result, status)
                }

            { state with
                repositoryTabs =
                    state.repositoryTabs
                    |> List.map (fun item ->
                        if item.id = id then
                            { item with
                                history = history
                                selection =
                                    item.selection
                                    |> Option.map (fun selection ->
                                        if not append && selection.commit.IsNone then
                                            { selection with
                                                requestId = None
                                                files = [||]
                                                groups = [||]
                                                diff = None
                                                hasLoaded = false
                                                error = None
                                            }
                                        else
                                            selection)
                            }
                        else
                            item)
            },
            Cmd.OfPromise.either read () (fun message -> message) (fun error ->
                if append then
                    OnRepositoryHistory(tab.id, requestId, true, Error error)
                else
                    OnRepositoryRefresh(tab.id, requestId, Error error, Error(string error)))
        | _ -> state, Cmd.none

    | OnRepositoryRefresh(id, requestId, result, status) ->
        match
            state.repositoryTabs
            |> List.tryFind (fun tab -> tab.id = id && tab.history.requestId = Some requestId)
        with
        | None -> state, Cmd.none
        | Some _ ->
            let updated, navigation =
                updateCore (OnRepositoryHistory(id, requestId, false, result)) state

            let tabs =
                updated.repositoryTabs
                |> List.map (fun tab ->
                    if tab.id = id then
                        { tab with workingTree = Some status }
                    else
                        tab)

            let selected =
                tabs
                |> List.exists (fun tab ->
                    tab.id = id
                    && (tab.selection
                        |> Option.exists (fun selection -> selection.commit.IsNone || not selection.hasLoaded)))

            { updated with repositoryTabs = tabs },
            Cmd.batch
                [
                    navigation
                    if selected then
                        Cmd.ofMsg (RefreshCommitFiles id)
                ]

    | OnRepositoryHistory(id, requestId, append, result) ->
        let tabs =
            state.repositoryTabs
            |> List.map (fun tab ->
                if tab.id <> id || tab.history.requestId <> Some requestId then
                    tab
                else
                    let history =
                        match result with
                        | Ok(repoId, page) ->
                            {
                                repoId = Some repoId
                                commits =
                                    (if append then
                                         Array.append tab.history.commits page.commits
                                     else
                                         page.commits)
                                    |> Array.distinctBy (fun commit -> commit.oid)
                                nextCursor = page.nextCursor
                                requestId = None
                                hasLoaded = true
                                error = None
                            }
                        | Error error ->
                            // Any failed page can mean a lost session or expired cursor.
                            // Refresh must reopen the root and start a new snapshot.
                            { tab.history with
                                repoId = None
                                nextCursor = None
                                requestId = None
                                error = Some(string error)
                            }

                    { tab with history = history })

        let navigation =
            state.repositoryTabs
            |> List.tryFind (fun tab -> tab.id = id && tab.history.requestId = Some requestId)
            |> Option.bind (fun tab -> tab.parentNavigation)
            |> Option.map (fun oid -> Cmd.ofMsg (ContinueParentNavigation(id, oid)))
            |> Option.defaultValue Cmd.none

        { state with repositoryTabs = tabs }, navigation

    | RefreshGitStatuses ->
        state,
        Cmd.batch
            [
                for project in state.projects do
                    Cmd.ofMsg (RefreshProjectGit project.id)
                match state.activeRepositoryTab with
                | Some id -> Cmd.ofMsg (RefreshRepositoryHistory id)
                | None -> ()
            ]

    | RefreshProjectGit id ->
        match state.projects |> List.tryFind (fun project -> project.id = id) with
        | Some project when AppTauri.Core.isAvailable () ->
            let previous = state.projectGit |> Map.tryFind id

            if
                previous
                |> Option.exists (fun check ->
                    check.isLoading && check.path = project.path && check.remote = project.remote)
            then
                state, Cmd.none
            else
                let requestId = Guid.NewGuid().ToString()

                let check =
                    {
                        requestId = requestId
                        path = project.path
                        remote = project.remote
                        isLoading = true
                        result = None
                        error = None
                    }

                { state with
                    projectGit = state.projectGit |> Map.add id check
                },
                Cmd.OfPromise.either
                    AppTauri.Git.projectStatus
                    id
                    (fun result -> OnProjectGitStatus(id, requestId, Ok result))
                    (fun error -> OnProjectGitStatus(id, requestId, Error error))
        | _ -> state, Cmd.none

    | OnProjectGitStatus(id, requestId, result) ->
        match state.projectGit |> Map.tryFind id with
        | Some check when
            check.requestId = requestId
            && (state.projects
                |> List.exists (fun project ->
                    project.id = id && project.path = check.path && project.remote = check.remote))
            ->
            let updated =
                match result with
                | Ok result ->
                    { check with
                        isLoading = false
                        result = Some result
                        error = None
                    }
                | Error error ->
                    { check with
                        isLoading = false
                        result = None
                        error = Some(string error)
                    }

            { state with
                projectGit = state.projectGit |> Map.add id updated
            },
            Cmd.none
        | _ -> state, Cmd.none

    | GetDataDir ->
        let getDataDirPromise () =
            promise {
                do! Promise.sleep 50
                let! (response: string) = AppTauri.Core.getDataDir ()
                return response
            }

        state, Cmd.OfPromise.either getDataDirPromise () OnGetDataDir OnGetDataDirError

    | OnGetDataDir response ->
        let newState = { state with appDataDir = response }
        newState, Cmd.none

    | OnGetDataDirError exn ->
        let e = sprintf "Error: %A" exn
        let newState = { state with appDataDir = e }
        newState, Cmd.none

    | GetConfigDir ->
        let getConfigDirPromise () =
            promise {
                do! Promise.sleep 50
                let! (response: string) = AppTauri.Core.getConfigDir ()
                return response
            }

        state, Cmd.OfPromise.either getConfigDirPromise () OnGetConfigDir OnGetConfigDirError
    | OnGetConfigDir response ->
        let newState = { state with appConfigDir = response }
        newState, Cmd.none
    | OnGetConfigDirError exn ->
        let e = sprintf "Error: %A" exn
        let newState = { state with appConfigDir = e }
        newState, Cmd.none

    | GetAppVersion ->
        let getAppVersionPromise () =
            promise {
                do! Promise.sleep 50
                let! (response: string) = AppTauri.Core.getAppVersion ()
                return response
            }

        state, Cmd.OfPromise.either getAppVersionPromise () OnAppVersion OnAppVersionError
    | OnAppVersion response ->
        let newState = { state with appVersion = response }
        newState, Cmd.none
    | OnAppVersionError exn ->
        let e = sprintf "Error: %A" exn
        let newState = { state with appVersion = e }
        newState, Cmd.none

    | GetTauriVersion ->
        let getTauriVersionPromise () =
            promise {
                do! Promise.sleep 50
                let! (response: string) = AppTauri.Core.getTauriVersion ()
                return response
            }

        state, Cmd.OfPromise.either getTauriVersionPromise () OnGetTauriVersion OnGetTauriVersionError

    | OnGetTauriVersion response ->
        let newState = { state with tauriVersion = response }
        newState, Cmd.none
    | OnGetTauriVersionError exn ->
        let e = sprintf "Error: %A" exn
        let newState = { state with tauriVersion = e }
        newState, Cmd.none

    | GetProjects timeout ->
        state,
        Cmd.OfPromise.either AppTauri.Projects.getProjectsAfterStartup timeout OnGetProjectsSuccess OnGetProjectsError
    | OnGetProjectsSuccess response ->
        let mutable prj: ProjectData list = []

        for r in response do
            try
                let pd = ProjectData.fromJson r
                prj <- List.append prj [ pd ]
            with ex ->
                FrontendLog.error $"Could not decode project record: {ex}"

        // sort by last opened
        prj <- prj |> List.sortByDescending (fun p -> p.lastOpened)

        let newState =
            { state with
                projectStorageError = None
                projectGit =
                    state.projectGit
                    |> Map.filter (fun id _ -> prj |> List.exists (fun project -> project.id = id))
                projects = prj
            }

        newState, Cmd.ofMsg RefreshGitStatuses
    | OnGetProjectsError exn ->
        let e = sprintf "Error: %A" exn
        FrontendLog.error $"Could not load projects: {exn}"

        let newState =
            { state with
                projectStorageError = Some e
            }

        newState, Cmd.none

    | OpenProject id ->
        let openProjectPromise p =
            promise {
                do! AppTauri.Projects.openProject p
                return id
            }

        state, Cmd.OfPromise.either openProjectPromise id OnOpenProjectSuccess OnOpenProjectError

    | OnOpenProjectSuccess id ->
        let projects = state.projects
        // get the project by id
        let prj = projects |> List.find (fun p -> p.id = id)
        let prj = { prj with lastOpened = DateTime.Now }
        let newProjects = projects |> List.map (fun p -> if p.id = id then prj else p)

        let prjJson = ProjectData.toJson prj

        let newState = { state with projects = newProjects }
        newState, Cmd.ofMsg (AddOrUpdateProject(id, prjJson))

    | OnOpenProjectError exn ->
        FrontendLog.error $"Could not open project: {exn}"
        state, Cmd.none

    | AddOrUpdateProject(id, json) ->
        let addOrUpdateProjectPromise (i, j) =
            promise {
                do! AppTauri.Projects.saveProject i j
                return id
            }

        state,
        Cmd.OfPromise.either addOrUpdateProjectPromise (id, json) OnAddOrUpdateProjectSuccess OnAddOrUpdateProjectError
    | OnAddOrUpdateProjectSuccess _id -> state, Cmd.ofMsg (GetProjects 0)
    | OnAddOrUpdateProjectError exn ->
        let e = sprintf "Error: %A" exn
        FrontendLog.error $"Could not update project: {exn}"

        let newState =
            { state with
                projectStorageError = Some e
            }

        newState, Cmd.none

    | OpenAddProjectModal ->
        if state.isProjectSaving || state.projectEditor.IsSome || state.isAboutOpen then
            state, Cmd.none
        else
            let project =
                { ProjectData.Default() with
                    id = Guid.NewGuid().ToString()
                    ide = "vscode"
                }

            { state with
                projectEditor = Some project
                projectSaveError = None
            },
            Cmd.none

    | OpenEditProjectModal id ->
        if state.isProjectSaving then
            state, Cmd.none
        else
            match state.projects |> List.tryFind (fun project -> project.id = id) with
            | Some project ->
                { state with
                    projectEditor = Some project
                    projectSaveError = None
                },
                Cmd.none
            | None -> state, Cmd.none

    | CloseProjectEditor ->
        if state.isProjectSaving then
            state, Cmd.none
        else
            { state with
                projectEditor = None
                projectSaveError = None
            },
            Cmd.none

    | SaveProject draft ->
        match state.projectEditor with
        | Some original when original.id = draft.id && not state.isProjectSaving ->
            if String.IsNullOrWhiteSpace draft.name || String.IsNullOrWhiteSpace draft.path then
                { state with
                    projectSaveError = Some "Name and project path are required."
                },
                Cmd.none
            else
                let latest =
                    state.projects
                    |> List.tryFind (fun project -> project.id = original.id)
                    |> Option.defaultValue original

                let project =
                    { draft with
                        id = original.id
                        lastOpened = latest.lastOpened
                        name = draft.name.Trim()
                        path = draft.path.Trim()
                    }

                let save (project: ProjectData) =
                    promise {
                        do! AppTauri.Projects.saveProject project.id (ProjectData.toJson project)
                        return project
                    }

                { state with
                    isProjectSaving = true
                    projectSaveError = None
                },
                Cmd.OfPromise.either save project OnSaveProjectSuccess OnSaveProjectError
        | _ -> state, Cmd.none

    | OnSaveProjectSuccess project ->
        let projects =
            if state.projects |> List.exists (fun existing -> existing.id = project.id) then
                state.projects
                |> List.map (fun existing -> if existing.id = project.id then project else existing)
            else
                project :: state.projects

        { state with
            projects = projects |> List.sortByDescending (fun project -> project.lastOpened)
            projectEditor = None
            isProjectSaving = false
            projectSaveError = None
        },
        Cmd.ofMsg (RefreshProjectGit project.id)

    | OnSaveProjectError error ->
        // Tauri can reject with a plain string rather than a JavaScript Error.
        { state with
            isProjectSaving = false
            projectSaveError = Some $"Could not save project: {error}"
        },
        Cmd.none

    | WindowSave ->
        if state.isWindowClosePending || not (AppTauri.Core.isAvailable ()) then
            state, Cmd.none
        else
            allowWindowClose <- false

            { state with
                isWindowClosePending = true
            },
            Cmd.ofMsg ContinueWindowClose

    | ContinueWindowClose ->
        if
            not state.isWindowClosePending
            || state.workspaceLoading
            || state.workspaceSaving.IsSome
        then
            state, Cmd.none
        elif
            not (isStorageReadOnly state)
            && state.workspaceReady
            && state.workspaceSavedRevision < state.workspaceRevision
        then
            state, Cmd.ofMsg SaveWorkspace
        else
            state, Cmd.ofMsg SaveWindowAndClose

    | SaveWindowAndClose ->
        state, Cmd.OfPromise.either AppTauri.Window.saveState () (fun () -> OnWindowSaveSuccess) OnWindowSaveError

    | OnWindowSaveSuccess ->
        if
            state.workspaceSaving.IsSome
            || (not (isStorageReadOnly state)
                && state.workspaceReady
                && state.workspaceSavedRevision < state.workspaceRevision)
        then
            state, Cmd.ofMsg ContinueWindowClose
        else
            let closeWindowPromise () =
                promise {
                    do! FrontendLog.flush ()
                    allowWindowClose <- true
                    do! AppTauri.Window.closeCurrent ()
                }

            state, Cmd.OfPromise.either closeWindowPromise () (fun () -> Empty) OnWindowCloseError


    | OnWindowSaveError exn ->
        let e = sprintf "Error while saving window state before close: %A" exn
        FrontendLog.error e
        allowWindowClose <- false

        let newState =
            { state with
                isWindowClosePending = false
            }

        newState, Cmd.none

    | OnWindowCloseError exn ->
        let e = sprintf "Error while closing window after save: %A" exn
        FrontendLog.error e
        allowWindowClose <- false

        let newState =
            { state with
                isWindowClosePending = false
            }

        newState, Cmd.none

    | WindowResized size ->
        let newState = { state with appWindowSize = Some size }

#if DEBUG
        FrontendLog.debug $"Window resized to: {size.width} x {size.height}"
#endif
        newState, Cmd.none

    | WindowMoved position ->
        let newState =
            { state with
                appWindowPosition = Some position
            }

#if DEBUG
        FrontendLog.debug $"Window moved to: {position.x}, {position.y}"
#endif
        newState, Cmd.none

    | Tick time ->
        let newState = { state with currentTime = time }
        newState, Cmd.none

/// Serialize durable choices only when they change. One writer is in flight;
/// newer choices are coalesced and flushed after its acknowledgement.
let update msg state =
    let prepared =
        match msg with
        | SelectRepositoryCommit(id, _)
        | SelectWorkingTree id ->
            { state with
                repositoryTabs =
                    state.repositoryTabs
                    |> List.map (fun tab ->
                        if tab.id = id then
                            { tab with
                                parentNavigation = None
                                parentNavigationError = None
                            }
                        else
                            tab)
            }
        | _ -> state

    let next, command = updateCore msg prepared

    match msg with
    | OnWorkspaceLoaded _ -> next, command
    | _ ->
        if workspaceSnapshot state = workspaceSnapshot next then
            next, command
        else
            let changed =
                { next with
                    workspaceRevision = next.workspaceRevision + 1
                }

            changed,
            Cmd.batch
                [
                    command
                    if changed.workspaceReady && not (isStorageReadOnly changed) then
                        Cmd.ofMsg SaveWorkspace
                ]

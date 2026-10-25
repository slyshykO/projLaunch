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
        watchRoots: (GitProtocol.Endpoint option * string) array
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
        endpoint: GitProtocol.Endpoint option
        id: string
        root: string
        name: string
        filesWidth: float
        filesHeight: float
        restoreFile: Workspace.File option
        historyView: Workspace.History
        restoringHistory: bool
        scrollRestoreId: string option
        commitNavigation: string option
        commitNavigationError: string option
        navigationLabel: string
        revealRevision: int
        branches: GitProtocol.BranchList option
        branchesError: string option
        history: RepositoryHistory
        workingTree: Result<GitProtocol.Status, string> option
        selection: CommitFiles option
        historyInvalidated: bool
        statusInvalidated: bool
        autoRefreshing: bool
        checkedFiles: Set<GitProtocol.DiffTarget * string>
        indexRequestId: string option
        indexError: string option
    }

type Msg =
    | Empty

    | Start
    | OnStart of unit

    | LoadWorkspace
    | OnStorageAccess of Result<AppTauri.Storage.Access, exn>
    | OnWorkspaceLoaded of int * Result<Workspace.Snapshot option, exn>
    | SaveWorkspace
    | SaveScrolledWorkspace of int
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
    | RefreshWatchedRepository of string
    | GitChanged of GitProtocol.Endpoint option * string * bool * bool
    | GitWatchStatus of GitProtocol.Endpoint option * string * string option
    | ApplyGitChanges
    | LoadMoreCommits of string
    | ContinueHistoryRestore of string
    | CancelHistoryRestore of string
    | HistoryScrolled of string * Workspace.Scroll
    | HistoryScrollRestored of string * string * Workspace.Scroll
    | OnRepositoryHistory of string * string * bool * Result<string * GitProtocol.LogPage, exn>
    | OnRepositoryRefresh of
        string *
        string *
        Result<string * GitProtocol.LogPage, exn> *
        Result<GitProtocol.Status, string> *
        Result<GitProtocol.BranchList, string>
    | SelectWorkingTree of string
    | SelectWorkingTreeFile of string * GitProtocol.DiffTarget * string
    | CheckWorkingTreeFile of string * GitProtocol.DiffTarget * string * bool
    | ChangeIndex of string * GitProtocol.DiffTarget * string array
    | OnIndexChanged of
        GitProtocol.Endpoint option *
        string *
        string *
        GitProtocol.DiffTarget *
        string array *
        Result<bool, exn>
    | SelectRepositoryCommit of tabId: string * oid: string
    | NavigateToParent of tabId: string * oid: string
    | NavigateToBranch of tabId: string * refName: string
    | BeginCommitNavigation of tabId: string * oid: string * label: string * fresh: bool
    | ContinueCommitNavigation of tabId: string * oid: string
    | CancelCommitNavigation of tabId: string
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

    | UpdateProjectLastOpened of string * DateTime
    | OnUpdateProjectLastOpenedSuccess of string
    | OnUpdateProjectLastOpenedError of exn
    | OpenAddProjectModal
    | CloseProjectEditor
    | OpenEditProjectModal of string
    | SaveProject of ProjectData
    | OnSaveProjectSuccess of ProjectData
    | OnSaveProjectError of exn
    | OpenDeleteProjectModal of string
    | CancelProjectDeletion
    | DeleteProject
    | OnProjectDeleted of string * Result<unit, exn>

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
        pendingProjectGit: Set<string>
        gitWatchErrors: Map<string, string>
        pendingIndexChanges: Map<string, string>
        projectEditor: ProjectData option
        isProjectSaving: bool
        projectSaveError: string option
        projectDeletion: ProjectData option
        isProjectDeleting: bool
        projectDeleteError: string option
        deletedProjectIds: Set<string>
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
            pendingProjectGit = Set.empty
            gitWatchErrors = Map.empty
            pendingIndexChanges = Map.empty
            projectEditor = None
            isProjectSaving = false
            projectSaveError = None
            projectDeletion = None
            isProjectDeleting = false
            projectDeleteError = None
            deletedProjectIds = Set.empty
            randomSalt = randomSalt
            appWindowSize = None
            appWindowPosition = None
            isWindowClosePending = false
        }

    state, Cmd.ofMsg Start

let private newRepositoryTab endpoint root name =
    {
        endpoint = endpoint
        id = Guid.NewGuid().ToString()
        root = root
        name = name
        filesWidth = 40.0
        filesHeight = 40.0
        restoreFile = None
        historyView = Workspace.emptyHistory
        restoringHistory = false
        scrollRestoreId = None
        commitNavigation = None
        commitNavigationError = None
        navigationLabel = "parent"
        revealRevision = 0
        branches = None
        branchesError = None
        selection = None
        workingTree = None
        historyInvalidated = false
        statusInvalidated = false
        autoRefreshing = false
        checkedFiles = Set.empty
        indexRequestId = None
        indexError = None
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

/// Writes are serialized in the UI per endpoint/worktree, including closed/reopened tabs.
let canChangeIndex (tab: RepositoryTab) =
    tab.indexRequestId.IsNone
    && tab.history.requestId.IsNone
    && not tab.statusInvalidated
    && (tab.selection
        |> Option.exists (fun selection ->
            selection.commit.IsNone
            && selection.hasLoaded
            && selection.requestId.IsNone
            && selection.error.IsNone))

/// One observation per endpoint/worktree, shared by cards and open tabs.
let watchedRepositories (state: State) =
    [
        for tab in state.repositoryTabs do
            yield tab.endpoint, tab.root
        for project in state.projects do
            match state.projectGit |> Map.tryFind project.id with
            | Some check when check.path = project.path && check.remote = project.remote -> yield! check.watchRoots
            | _ -> ()
    ]
    |> List.distinctBy (fun (endpoint, root) -> GitProtocol.repositoryKey endpoint root)

let workspaceSnapshot (state: State) : Workspace.Snapshot =
    {
        version = Workspace.CurrentVersion
        activeEndpoint =
            state.repositoryTabs
            |> List.tryFind (fun tab -> Some tab.id = state.activeRepositoryTab)
            |> Option.bind (fun tab -> tab.endpoint)
        activeRoot =
            state.repositoryTabs
            |> List.tryFind (fun tab -> Some tab.id = state.activeRepositoryTab)
            |> Option.map (fun tab -> tab.root)
        tabs =
            state.repositoryTabs
            |> List.map (fun tab ->
                ({
                    Workspace.Tab.root = tab.root
                    endpoint = tab.endpoint
                    name = tab.name
                    filesWidth = tab.filesWidth
                    filesHeight = tab.filesHeight
                    history = Some tab.historyView
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
    { newRepositoryTab saved.endpoint saved.root saved.name with
        filesWidth = Workspace.panelSize saved.filesWidth
        filesHeight = Workspace.panelSize saved.filesHeight
        restoreFile = saved.selection |> Option.bind (fun selection -> selection.file)
        historyView = saved.history |> Option.defaultValue Workspace.emptyHistory
        restoringHistory = saved.history |> Option.exists (fun history -> history.count > 0)
        scrollRestoreId = saved.history |> Option.map (fun _ -> Guid.NewGuid().ToString())
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
    | UpdateProjectLastOpened _
    | OpenDeleteProjectModal _
    | DeleteProject
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
                               state.repositoryTabs
                               |> List.forall (fun existing ->
                                   existing.root <> tab.root
                                   || GitProtocol.endpointKey existing.endpoint
                                      <> GitProtocol.endpointKey tab.endpoint)))
                    else
                        restored

                let active =
                    if changed then
                        state.activeRepositoryTab
                    else
                        snapshot
                        |> Option.bind (fun saved ->
                            tabs
                            |> List.tryFind (fun tab ->
                                Some tab.root = saved.activeRoot
                                && GitProtocol.endpointKey tab.endpoint = GitProtocol.endpointKey saved.activeEndpoint))
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

    | SaveScrolledWorkspace revision ->
        state,
        if revision = state.workspaceRevision && state.workspaceError.IsNone then
            Cmd.ofMsg SaveWorkspace
        else
            Cmd.none

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
        match
            state.repositoryTabs
            |> List.tryFind (fun tab ->
                tab.root = repository.root
                && GitProtocol.endpointKey tab.endpoint = GitProtocol.endpointKey repository.endpoint)
        with
        | Some tab ->
            { state with
                activeRepositoryTab = Some tab.id
            },
            if not tab.history.hasLoaded && tab.history.requestId.IsNone then
                Cmd.ofMsg (RefreshRepositoryHistory tab.id)
            elif tab.restoringHistory then
                Cmd.ofMsg (ContinueHistoryRestore tab.id)
            else
                Cmd.none
        | None ->
            let tab =
                { newRepositoryTab repository.endpoint repository.root repository.name with
                    indexRequestId =
                        state.pendingIndexChanges
                        |> Map.tryFind (GitProtocol.repositoryKey repository.endpoint repository.root)
                }

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
            elif tab.restoringHistory then
                Cmd.ofMsg (ContinueHistoryRestore id)
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
        if state.projectEditor.IsSome || state.projectDeletion.IsSome then
            state, Cmd.none
        else
            { state with isAboutOpen = true }, Cmd.none
    | CloseAbout -> { state with isAboutOpen = false }, Cmd.none

    | CheckWorkingTreeFile(id, target, path, checked') ->
        let tabs =
            state.repositoryTabs
            |> List.map (fun tab ->
                if
                    tab.id = id
                    && canChangeIndex tab
                    && (tab.selection.Value.groups
                        |> Array.exists (fun (group, files) ->
                            group = target && (files |> Array.exists (fun file -> file.path = path))))
                then
                    { tab with
                        checkedFiles = (if checked' then Set.add else Set.remove) (target, path) tab.checkedFiles
                    }
                else
                    tab)

        { state with repositoryTabs = tabs }, Cmd.none

    | ChangeIndex(id, target, paths) ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab when canChangeIndex tab && not state.isWindowClosePending ->
            let key = GitProtocol.repositoryKey tab.endpoint tab.root

            let files =
                tab.selection.Value.groups
                |> Array.tryFind (fun (group, _) -> group = target)
                |> Option.map snd
                |> Option.defaultValue [||]

            let paths = Array.distinct paths

            if
                paths.Length = 0
                || Map.containsKey key state.pendingIndexChanges
                || (paths
                    |> Array.exists (fun path -> files |> Array.exists (fun file -> file.path = path) |> not))
            then
                state, Cmd.none
            else
                let requestId = Guid.NewGuid().ToString()

                let write () =
                    promise {
                        let! repository =
                            AppTauri.Git.openRepository tab.endpoint tab.root $"index-{requestId}-open"

                        return!
                            AppTauri.Git.changeIndex
                                tab.endpoint
                                repository.repoId
                                $"index-{requestId}-write"
                                target
                                paths
                    }

                let tabs =
                    state.repositoryTabs
                    |> List.map (fun item ->
                        if GitProtocol.repositoryKey item.endpoint item.root = key then
                            { item with
                                indexRequestId = Some requestId
                                indexError = None
                                selection =
                                    item.selection
                                    |> Option.map (fun selection ->
                                        if selection.commit.IsNone then
                                            { selection with
                                                diff =
                                                    selection.diff
                                                    |> Option.map (fun diff -> { diff with requestId = None })
                                            }
                                        else
                                            selection)
                            }
                        else
                            item)

                { state with
                    pendingIndexChanges = Map.add key requestId state.pendingIndexChanges
                    repositoryTabs = tabs
                },
                Cmd.OfPromise.either
                    write
                    ()
                    (fun result -> OnIndexChanged(tab.endpoint, tab.root, requestId, target, paths, Ok result))
                    (fun error -> OnIndexChanged(tab.endpoint, tab.root, requestId, target, paths, Error error))
        | _ -> state, Cmd.none

    | OnIndexChanged(endpoint, root, requestId, target, paths, result) ->
        let key = GitProtocol.repositoryKey endpoint root

        if Map.tryFind key state.pendingIndexChanges <> Some requestId then
            state, Cmd.none
        else
            let tabs =
                state.repositoryTabs
                |> List.map (fun tab ->
                    if GitProtocol.repositoryKey tab.endpoint tab.root <> key then
                        tab
                    else
                        let diff =
                            tab.selection
                            |> Option.filter (fun selection -> selection.commit.IsNone)
                            |> Option.bind (fun selection -> selection.diff)

                        let restore =
                            diff
                            |> Option.map (fun diff ->
                                let nextTarget =
                                    if
                                        result = Ok true
                                        && diff.target = target
                                        && Array.contains diff.file.path paths
                                    then
                                        if target = GitProtocol.Staged then
                                            GitProtocol.Unstaged
                                        else
                                            GitProtocol.Staged
                                    else
                                        diff.target

                                ({
                                    target = Workspace.targetName nextTarget
                                    path = diff.file.path
                                }
                                : Workspace.File))

                        { tab with
                            indexRequestId = None
                            indexError =
                                match result with
                                | Ok true -> None
                                | Ok false ->
                                    Some
                                        "Git did not confirm the operation. Check the refreshed changes before retrying."
                                | Error error -> Some $"{error}\nCheck the refreshed changes before retrying."
                            checkedFiles = if result = Ok true then Set.empty else tab.checkedFiles
                            restoreFile = restore
                            statusInvalidated = true
                            selection =
                                tab.selection
                                |> Option.map (fun selection ->
                                    if selection.commit.IsNone then
                                        { selection with requestId = None }
                                    else
                                        selection)
                        })

            { state with
                pendingIndexChanges = Map.remove key state.pendingIndexChanges
                repositoryTabs = tabs
            },
            Cmd.batch
                [
                    Cmd.ofMsg (GitChanged(endpoint, root, false, true))
                    // Explicit refresh also works when this tab is inactive or newly reopened.
                    for tab in tabs do
                        if GitProtocol.repositoryKey tab.endpoint tab.root = key then
                            Cmd.ofMsg (RefreshWatchedRepository tab.id)
                ]

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
                                checkedFiles = Set.empty
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
            updateCore (BeginCommitNavigation(id, oid, "parent", false)) state
        | _ -> state, Cmd.none

    | NavigateToBranch(id, refName) ->
        let branch =
            state.repositoryTabs
            |> List.tryFind (fun tab -> tab.id = id)
            |> Option.bind (fun tab -> tab.branches)
            |> Option.bind (fun data -> data.branches |> Array.tryFind (fun branch -> branch.refName = refName))

        match branch with
        | Some branch -> updateCore (BeginCommitNavigation(id, branch.oid, $"branch {branch.name}", true)) state
        | None -> state, Cmd.none

    | BeginCommitNavigation(id, oid, label, fresh) ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab ->
            let state, _ = updateCore (CancelHistoryRestore id) state

            let next =
                { state with
                    repositoryTabs =
                        state.repositoryTabs
                        |> List.map (fun item ->
                            if item.id = id then
                                { item with
                                    commitNavigation = Some oid
                                    commitNavigationError = None
                                    navigationLabel = label
                                }
                            else
                                item)
                }

            if tab.history.commits |> Array.exists (fun commit -> commit.oid = oid) then
                updateCore (ContinueCommitNavigation(id, oid)) next
            elif tab.history.requestId.IsSome then
                next, Cmd.none
            elif
                fresh
                || tab.history.error.IsSome
                || not tab.history.hasLoaded
                || tab.history.nextCursor.IsNone
            then
                next, Cmd.ofMsg (RefreshRepositoryHistory id)
            else
                next, Cmd.ofMsg (ContinueCommitNavigation(id, oid))
        | _ -> state, Cmd.none

    | CancelCommitNavigation id ->
        { state with
            repositoryTabs =
                state.repositoryTabs
                |> List.map (fun tab ->
                    if tab.id = id then
                        { tab with
                            commitNavigation = None
                            commitNavigationError = None
                        }
                    else
                        tab)
        },
        Cmd.none

    | ContinueCommitNavigation(id, oid) ->
        match
            state.repositoryTabs
            |> List.tryFind (fun tab -> tab.id = id && tab.commitNavigation = Some oid)
        with
        | Some tab ->
            if tab.history.commits |> Array.exists (fun commit -> commit.oid = oid) then
                let revealed =
                    { state with
                        repositoryTabs =
                            state.repositoryTabs
                            |> List.map (fun item ->
                                if item.id = id then
                                    { item with
                                        revealRevision = item.revealRevision + 1
                                        selection =
                                            item.selection
                                            |> Option.map (fun selection -> { selection with diff = None })
                                    }
                                else
                                    item)
                    }

                updateCore (SelectRepositoryCommit(id, oid)) revealed
            elif tab.history.requestId.IsSome then
                state, Cmd.none
            elif tab.history.error.IsNone && tab.history.nextCursor.IsSome then
                state, Cmd.ofMsg (LoadMoreCommits id)
            else
                let error =
                    match tab.history.error with
                    | Some error -> $"Could not open {tab.navigationLabel}. Select it again to retry. {error}"
                    | None ->
                        $"The commit for {tab.navigationLabel} ({oid}) is not available in this repository's history. It may be missing from a shallow clone or no longer reachable."

                { state with
                    repositoryTabs =
                        state.repositoryTabs
                        |> List.map (fun item ->
                            if item.id = id then
                                { item with
                                    commitNavigation = None
                                    commitNavigationError = Some error
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
            { state with
                repositoryTabs =
                    state.repositoryTabs
                    |> List.map (fun item ->
                        if item.id = id then
                            { item with
                                commitNavigation = None
                                commitNavigationError = None
                            }
                        else
                            item)
            },
            Cmd.none
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
                                    commitNavigation = None
                                    commitNavigationError = None
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
                && (selection.commit.IsSome || tab.indexRequestId.IsNone)
                && (selection.commit
                    |> Option.forall (fun commit -> commit.parents.Length <= 1 || selection.parent.IsSome))
                ->
                let requestId = Guid.NewGuid().ToString()
                let preserveDiff = selection.commit.IsNone && tab.restoreFile.IsSome

                let next =
                    { selection with
                        requestId = Some requestId
                        diff = if preserveDiff then selection.diff else None
                        files = if preserveDiff then selection.files else [||]
                        groups = if preserveDiff then selection.groups else [||]
                        hasLoaded = preserveDiff && selection.hasLoaded
                        error = None
                    }

                let read () =
                    promise {
                        // Reopen by root so retry also recovers from a restarted server.
                        let! repository =
                            AppTauri.Git.openRepository tab.endpoint tab.root $"files-{requestId}-open"

                        let targets =
                            match selection.commit with
                            | Some commit -> [| GitProtocol.Commit(commit.oid, selection.parent) |]
                            | None -> [| GitProtocol.Staged; GitProtocol.Unstaged; GitProtocol.Untracked |]

                        let groups = ResizeArray<_>()

                        for index, target in targets |> Array.indexed do
                            let! files =
                                AppTauri.Git.diffFiles
                                    tab.endpoint
                                    repository.repoId
                                    $"files-{requestId}-{index}"
                                    target

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
                                    let matching =
                                        groups
                                        |> Array.filter (fun (_, files) ->
                                            files |> Array.exists (fun entry -> entry.path = file.path))

                                    let chosen =
                                        matching
                                        |> Array.tryFind (fun (group, _) -> group = target)
                                        |> Option.orElseWith (fun () ->
                                            if selection.commit.IsNone then
                                                Array.tryHead matching
                                            else
                                                None)

                                    match chosen with
                                    | Some(target, _) ->
                                        restore <-
                                            Cmd.ofMsg (
                                                if selection.commit.IsSome then
                                                    SelectCommitFile(id, file.path)
                                                else
                                                    SelectWorkingTreeFile(id, target, file.path)
                                            )

                                        Some
                                            { file with
                                                target = Workspace.targetName target
                                            }
                                    | None -> None))

                        { tab with
                            restoreFile = pending
                            checkedFiles =
                                tab.checkedFiles
                                |> Set.filter (fun (target, path) ->
                                    groups
                                    |> Array.exists (fun (group, files) ->
                                        group = target && (files |> Array.exists (fun file -> file.path = path))))
                            selection =
                                Some
                                    { selection with
                                        groups = groups
                                        diff =
                                            if tab.restoreFile.IsSome && pending.IsNone then
                                                None
                                            else
                                                selection.diff
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
            | Some selection when
                selection.hasLoaded
                && selection.requestId.IsNone
                && (selection.commit.IsSome || tab.indexRequestId.IsNone)
                ->
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

                        if alreadySelected && tab.restoreFile.IsNone then
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
                            patch =
                                if tab.restoreFile.IsSome then
                                    selection.diff
                                    |> Option.filter (fun diff -> diff.target = target && diff.file.path = file.path)
                                    |> Option.bind (fun diff -> diff.patch)
                                else
                                    None
                            error = None
                        }

                    let read () =
                        promise {
                            let! repository =
                                AppTauri.Git.openRepository tab.endpoint tab.root $"diff-{requestId}-open"

                            return!
                                AppTauri.Git.diff
                                    tab.endpoint
                                    repository.repoId
                                    $"diff-{requestId}-patch"
                                    target
                                    file.path
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

    | HistoryScrolled(id, scroll)
    | HistoryScrollRestored(id, _, scroll) ->
        let tabs =
            state.repositoryTabs
            |> List.map (fun tab ->
                let accepted =
                    match msg with
                    | HistoryScrollRestored(_, token, _) ->
                        tab.scrollRestoreId = Some token && not tab.restoringHistory
                    | _ -> state.activeRepositoryTab = Some id

                if tab.id = id && accepted then
                    { tab with
                        historyView =
                            {
                                count = tab.history.commits.Length
                                scroll = scroll
                            }
                        restoringHistory = false
                        scrollRestoreId = None
                    }
                else
                    tab)

        { state with repositoryTabs = tabs }, Cmd.none

    | CancelHistoryRestore id ->
        { state with
            repositoryTabs =
                state.repositoryTabs
                |> List.map (fun tab ->
                    if tab.id = id && (tab.restoringHistory || tab.scrollRestoreId.IsSome) then
                        { tab with
                            restoringHistory = false
                            scrollRestoreId = None
                            historyView =
                                {
                                    count = tab.history.commits.Length
                                    scroll = Workspace.emptyScroll
                                }
                        }
                    else
                        tab)
        },
        Cmd.none

    | ContinueHistoryRestore id ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab when
            tab.restoringHistory
            && state.activeRepositoryTab = Some id
            && not state.isWindowClosePending
            && tab.history.requestId.IsNone
            && tab.history.error.IsNone
            && tab.history.hasLoaded
            ->
            updateCore (LoadMoreCommits id) state
        | _ -> state, Cmd.none

    | GitWatchStatus(endpoint, root, error) ->
        let key = GitProtocol.repositoryKey endpoint root

        { state with
            gitWatchErrors =
                match error with
                | Some error -> state.gitWatchErrors |> Map.add key error
                | None -> state.gitWatchErrors |> Map.remove key
        },
        Cmd.none

    | GitChanged(endpoint, root, history, workingTree) ->
        let matches otherEndpoint otherRoot =
            GitProtocol.repositoryKey endpoint root = GitProtocol.repositoryKey otherEndpoint otherRoot

        let projects =
            state.projectGit
            |> Map.toSeq
            |> Seq.choose (fun (id, check) ->
                if check.watchRoots |> Array.exists (fun (endpoint, root) -> matches endpoint root) then
                    Some id
                else
                    None)
            |> Set.ofSeq

        { state with
            pendingProjectGit = Set.union state.pendingProjectGit projects
            repositoryTabs =
                state.repositoryTabs
                |> List.map (fun tab ->
                    if matches tab.endpoint tab.root then
                        { tab with
                            historyInvalidated = tab.historyInvalidated || history
                            statusInvalidated = tab.statusInvalidated || workingTree || history
                        }
                    else
                        tab)
        },
        Cmd.ofMsg ApplyGitChanges

    | ApplyGitChanges ->
        if state.isWindowClosePending then
            state, Cmd.none
        else
            let projects =
                state.pendingProjectGit
                |> Set.filter (fun id ->
                    state.projectGit
                    |> Map.tryFind id
                    |> Option.forall (fun check -> not check.isLoading))

            { state with
                pendingProjectGit = Set.difference state.pendingProjectGit projects
            },
            Cmd.batch
                [
                    for id in projects do
                        Cmd.ofMsg (RefreshProjectGit id)
                    for tab in state.repositoryTabs do
                        if
                            state.activeRepositoryTab = Some tab.id
                            && (tab.historyInvalidated || tab.statusInvalidated)
                            && (tab.history.hasLoaded || tab.history.error.IsSome)
                            && tab.history.requestId.IsNone
                            && tab.indexRequestId.IsNone
                            && not tab.restoringHistory
                            && tab.commitNavigation.IsNone
                            && (tab.selection
                                |> Option.forall (fun selection ->
                                    selection.requestId.IsNone
                                    && (selection.diff |> Option.forall (fun diff -> diff.requestId.IsNone))))
                        then
                            Cmd.ofMsg (RefreshWatchedRepository tab.id)
                ]

    | RefreshWatchedRepository id
    | RefreshRepositoryHistory id
    | LoadMoreCommits id ->
        let automatic =
            match msg with
            | RefreshWatchedRepository _ -> true
            | _ -> false

        let append =
            match msg with
            | LoadMoreCommits _ -> true
            | _ -> false

        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab when
            tab.history.requestId.IsNone
            && tab.indexRequestId.IsNone
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
                                let! repository =
                                    AppTauri.Git.openRepository tab.endpoint tab.root $"history-{requestId}-open"

                                return repository.repoId
                            }

                    let! result =
                        promise {
                            try
                                if automatic && not tab.historyInvalidated && tab.history.repoId = Some repoId then
                                    return
                                        Ok(
                                            repoId,
                                            ({
                                                commits = tab.history.commits
                                                nextCursor = tab.history.nextCursor
                                            }
                                            : GitProtocol.LogPage)
                                        )
                                else
                                    let! page =
                                        AppTauri.Git.log
                                            tab.endpoint
                                            repoId
                                            $"history-{requestId}-log"
                                            (if append then tab.history.nextCursor else None)
                                            100
                                    // Keep the current rows mounted until the replacement snapshot
                                    // has reached their depth. A failed page leaves them intact.
                                    let mutable page = page
                                    let mutable count = page.commits.Length

                                    while automatic && count < tab.history.commits.Length && page.nextCursor.IsSome do
                                        let! more =
                                            AppTauri.Git.log
                                                tab.endpoint
                                                repoId
                                                $"history-{requestId}-log-{count}"
                                                page.nextCursor
                                                100

                                        let commits =
                                            Array.append page.commits more.commits
                                            |> Array.distinctBy (fun commit -> commit.oid)

                                        if commits.Length <= count && more.nextCursor.IsSome then
                                            failwith "History pagination made no progress. Refresh to try again."

                                        count <- commits.Length

                                        page <-
                                            {
                                                commits = commits
                                                nextCursor = more.nextCursor
                                            }

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
                                    let! status = AppTauri.Git.status tab.endpoint repoId $"history-{requestId}-status"
                                    return Ok status
                                with error ->
                                    return Error(string error)
                            }

                        let! branches =
                            promise {
                                try
                                    let! branches =
                                        AppTauri.Git.branches tab.endpoint repoId $"history-{requestId}-branches"

                                    return Ok branches
                                with error ->
                                    return Error(string error)
                            }

                        return OnRepositoryRefresh(id, requestId, result, status, branches)
                }

            { state with
                repositoryTabs =
                    state.repositoryTabs
                    |> List.map (fun item ->
                        if item.id = id then
                            { item with
                                history = history
                                autoRefreshing = automatic
                                historyInvalidated = if append then item.historyInvalidated else false
                                statusInvalidated = if append then item.statusInvalidated else false
                                restoreFile =
                                    if automatic then
                                        item.restoreFile
                                        |> Option.orElseWith (fun () ->
                                            item.selection
                                            |> Option.filter (fun selection -> selection.commit.IsNone)
                                            |> Option.bind (fun selection -> selection.diff)
                                            |> Option.map (fun diff ->
                                                ({
                                                    target = Workspace.targetName diff.target
                                                    path = diff.file.path
                                                }
                                                : Workspace.File)))
                                    else
                                        item.restoreFile
                                branchesError = if append then item.branchesError else None
                                selection =
                                    item.selection
                                    |> Option.map (fun selection ->
                                        if not append && not automatic && selection.commit.IsNone then
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
                    OnRepositoryRefresh(tab.id, requestId, Error error, Error(string error), Error(string error)))
        | _ -> state, Cmd.none

    | OnRepositoryRefresh(id, requestId, result, status, branches) ->
        match
            state.repositoryTabs
            |> List.tryFind (fun tab -> tab.id = id && tab.history.requestId = Some requestId)
        with
        | None -> state, Cmd.none
        | Some original ->
            let updated, navigation =
                updateCore (OnRepositoryHistory(id, requestId, false, result)) state

            let tabs =
                updated.repositoryTabs
                |> List.map (fun tab ->
                    if tab.id = id then
                        { tab with
                            autoRefreshing = false
                            scrollRestoreId =
                                if original.autoRefreshing then
                                    Some(Guid.NewGuid().ToString())
                                else
                                    tab.scrollRestoreId
                            workingTree = Some status
                            branches =
                                match branches with
                                | Ok data -> Some data
                                | Error _ -> tab.branches
                            branchesError =
                                match branches with
                                | Ok _ -> None
                                | Error error -> Some error
                        }
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

                    let noProgress =
                        tab.restoringHistory
                        && append
                        && history.error.IsNone
                        && history.nextCursor.IsSome
                        && history.commits.Length <= tab.history.commits.Length

                    let history =
                        if noProgress then
                            { history with
                                error = Some "History pagination made no progress. Refresh to try again."
                            }
                        else
                            history

                    let restoring =
                        tab.restoringHistory
                        && (history.error.IsSome
                            || (history.commits.Length < tab.historyView.count && history.nextCursor.IsSome))

                    { tab with
                        history = history
                        restoringHistory = restoring
                        historyView =
                            if restoring || history.error.IsSome then
                                tab.historyView
                            else
                                { tab.historyView with
                                    count = history.commits.Length
                                }
                    })

        let navigation =
            state.repositoryTabs
            |> List.tryFind (fun tab -> tab.id = id && tab.history.requestId = Some requestId)
            |> Option.bind (fun tab -> tab.commitNavigation)
            |> Option.map (fun oid -> Cmd.ofMsg (ContinueCommitNavigation(id, oid)))
            |> Option.defaultValue Cmd.none

        let restore =
            if
                state.repositoryTabs
                |> List.exists (fun tab -> tab.id = id && tab.history.requestId = Some requestId)
            then
                match
                    tabs
                    |> List.tryFind (fun tab -> tab.id = id && tab.restoringHistory && tab.history.error.IsNone)
                with
                | Some _ -> Cmd.ofMsg (ContinueHistoryRestore id)
                | None -> Cmd.none
            else
                Cmd.none

        { state with repositoryTabs = tabs }, Cmd.batch [ navigation; restore ]

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
                        watchRoots =
                            previous
                            |> Option.filter (fun check -> check.path = project.path && check.remote = project.remote)
                            |> Option.map (fun check -> check.watchRoots)
                            |> Option.defaultValue [||]
                        result =
                            previous
                            |> Option.filter (fun check -> check.path = project.path && check.remote = project.remote)
                            |> Option.bind (fun check -> check.result)
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
                        watchRoots = result.repositories |> Array.map (fun repo -> repo.endpoint, repo.root)
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
        // A list requested before deletion can arrive after its acknowledgement.
        prj <-
            prj
            |> List.filter (fun p -> not (state.deletedProjectIds.Contains p.id))
            |> List.sortByDescending (fun p -> p.lastOpened)

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
        if
            state.isProjectDeleting
            || not (state.projects |> List.exists (fun p -> p.id = id))
        then
            state, Cmd.none
        else
            let openProjectPromise p =
                promise {
                    do! AppTauri.Projects.openProject p
                    return id
                }

            state, Cmd.OfPromise.either openProjectPromise id OnOpenProjectSuccess OnOpenProjectError

    | OnOpenProjectSuccess id ->
        match state.projects |> List.tryFind (fun p -> p.id = id) with
        | Some project when
            not (
                state.isProjectDeleting
                && (state.projectDeletion |> Option.exists (fun p -> p.id = id))
            )
            ->
            let project =
                { project with
                    lastOpened = DateTime.Now
                }

            let projects =
                state.projects |> List.map (fun p -> if p.id = id then project else p)

            { state with projects = projects }, Cmd.ofMsg (UpdateProjectLastOpened(id, project.lastOpened))
        | _ -> state, Cmd.none

    | OnOpenProjectError exn ->
        FrontendLog.error $"Could not open project: {exn}"
        state, Cmd.none

    | UpdateProjectLastOpened(id, lastOpened) ->
        if
            state.deletedProjectIds.Contains id
            || (state.isProjectDeleting
                && (state.projectDeletion |> Option.exists (fun p -> p.id = id)))
        then
            state, Cmd.none
        else
            let save () =
                promise {
                    do! AppTauri.Projects.updateLastOpened id lastOpened
                    return id
                }

            state, Cmd.OfPromise.either save () OnUpdateProjectLastOpenedSuccess OnUpdateProjectLastOpenedError
    | OnUpdateProjectLastOpenedSuccess id ->
        state,
        if state.deletedProjectIds.Contains id then
            Cmd.none
        else
            Cmd.ofMsg (GetProjects 0)
    | OnUpdateProjectLastOpenedError exn ->
        let e = sprintf "Error: %A" exn
        FrontendLog.error $"Could not update project: {exn}"

        let newState =
            { state with
                projectStorageError = Some e
            }

        newState, Cmd.none

    | OpenAddProjectModal ->
        if
            state.isProjectSaving
            || state.projectEditor.IsSome
            || state.isAboutOpen
            || state.projectDeletion.IsSome
            || state.isWindowClosePending
        then
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
        if
            state.isProjectSaving
            || state.projectDeletion.IsSome
            || state.isAboutOpen
            || state.isWindowClosePending
        then
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

    | OpenDeleteProjectModal id ->
        if
            state.projectEditor.IsSome
            || state.projectDeletion.IsSome
            || state.isAboutOpen
            || state.isWindowClosePending
        then
            state, Cmd.none
        else
            match state.projects |> List.tryFind (fun p -> p.id = id) with
            | Some project ->
                { state with
                    projectDeletion = Some project
                    projectDeleteError = None
                },
                Cmd.none
            | None -> state, Cmd.none

    | CancelProjectDeletion ->
        if state.isProjectDeleting then
            state, Cmd.none
        else
            { state with
                projectDeletion = None
                projectDeleteError = None
            },
            Cmd.none

    | DeleteProject ->
        match state.projectDeletion with
        | Some project when not state.isProjectDeleting && not state.isWindowClosePending ->
            { state with
                isProjectDeleting = true
                projectDeleteError = None
            },
            Cmd.OfPromise.either
                AppTauri.Projects.deleteProject
                project.id
                (fun () -> OnProjectDeleted(project.id, Ok()))
                (fun error -> OnProjectDeleted(project.id, Error error))
        | _ -> state, Cmd.none

    | OnProjectDeleted(id, result) ->
        if
            not state.isProjectDeleting
            || not (state.projectDeletion |> Option.exists (fun p -> p.id = id))
        then
            state, Cmd.none
        else
            match result with
            | Error error ->
                { state with
                    isProjectDeleting = false
                    projectDeleteError = Some $"Could not remove project: {error}"
                    isWindowClosePending = false
                },
                Cmd.none
            | Ok() ->
                { state with
                    projects = state.projects |> List.filter (fun p -> p.id <> id)
                    projectGit = state.projectGit |> Map.remove id
                    pendingProjectGit = state.pendingProjectGit |> Set.remove id
                    deletedProjectIds = state.deletedProjectIds |> Set.add id
                    projectDeletion = None
                    isProjectDeleting = false
                    projectDeleteError = None
                },
                Cmd.ofMsg ContinueWindowClose

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
            || state.isProjectDeleting
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
            state.isProjectDeleting
            || state.workspaceSaving.IsSome
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
        newState, Cmd.ofMsg ApplyGitChanges

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
                                commitNavigation = None
                                commitNavigationError = None
                                restoringHistory = false
                                scrollRestoreId = None
                                historyView =
                                    if tab.scrollRestoreId.IsSome then
                                        {
                                            count = tab.history.commits.Length
                                            scroll = Workspace.emptyScroll
                                        }
                                    else
                                        tab.historyView
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
                        match msg with
                        | HistoryScrolled _ ->
                            Cmd.OfPromise.perform
                                (fun () ->
                                    promise {
                                        do! Promise.sleep 200
                                        return changed.workspaceRevision
                                    })
                                ()
                                SaveScrolledWorkspace
                        | _ -> Cmd.ofMsg SaveWorkspace
                ]

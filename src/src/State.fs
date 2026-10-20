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
        requestId: string option
        patch: GitProtocol.Patch option
        error: string option
    }

type CommitFiles =
    {
        commit: GitProtocol.Commit
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
        history: RepositoryHistory
        selection: CommitFiles option
    }

type Msg =
    | Empty

    | Start
    | OnStart of unit

    | ShowProjects
    | OpenRepositoryTab of GitProtocol.RepositoryStatus
    | SelectRepositoryTab of string
    | CloseRepositoryTab of string
    | CloseActiveTab
    | RefreshRepositoryHistory of string
    | LoadMoreCommits of string
    | OnRepositoryHistory of string * string * bool * Result<string * GitProtocol.LogPage, exn>
    | SelectRepositoryCommit of tabId: string * oid: string
    | SelectCommitParent of tabId: string * parent: int
    | RefreshCommitFiles of tabId: string
    | OnCommitFiles of tabId: string * requestId: string * Result<GitProtocol.DiffFile array, exn>
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

let update msg state =
    match msg with
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
                    Cmd.ofMsg (GetProjects 50)
                    Cmd.ofMsg GetConfigDir
                    Cmd.ofMsg GetDataDir
                    Cmd.ofMsg GetAppVersion
                    Cmd.ofMsg GetTauriVersion
                ]
        else
            state, Cmd.none

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
            Cmd.none
        | None ->
            let tab =
                {
                    id = Guid.NewGuid().ToString()
                    root = repository.root
                    name = repository.name
                    selection = None
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

            { state with
                repositoryTabs = state.repositoryTabs @ [ tab ]
                activeRepositoryTab = Some tab.id
            },
            Cmd.ofMsg (RefreshRepositoryHistory tab.id)
    | SelectRepositoryTab id ->
        if state.repositoryTabs |> List.exists (fun tab -> tab.id = id) then
            { state with
                activeRepositoryTab = Some id
            },
            Cmd.none
        else
            state, Cmd.none
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
        Cmd.none
    | OpenAbout ->
        if state.projectEditor.IsSome then
            state, Cmd.none
        else
            { state with isAboutOpen = true }, Cmd.none
    | CloseAbout -> { state with isAboutOpen = false }, Cmd.none

    | SelectRepositoryCommit(id, oid) ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab when tab.selection |> Option.exists (fun selection -> selection.commit.oid = oid) -> state, Cmd.none
        | Some tab ->
            match tab.history.commits |> Array.tryFind (fun commit -> commit.oid = oid) with
            | Some commit ->
                let selection =
                    {
                        commit = commit
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
                                { item with selection = Some selection }
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
                && parent <= selection.commit.parents.Length
                && selection.parent <> Some parent
                ->
                let next =
                    { selection with
                        parent = Some parent
                        diff = None
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
                                { item with selection = Some next }
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
                && (selection.commit.parents.Length <= 1 || selection.parent.IsSome)
                ->
                let requestId = Guid.NewGuid().ToString()

                let next =
                    { selection with
                        requestId = Some requestId
                        diff = None
                        files = [||]
                        hasLoaded = false
                        error = None
                    }

                let read () =
                    promise {
                        // Reopen by root so retry also recovers from a restarted server.
                        let! repository = AppTauri.Git.openRepository tab.root $"files-{requestId}-open"

                        return!
                            AppTauri.Git.diffFiles
                                repository.repoId
                                $"files-{requestId}-list"
                                (GitProtocol.Commit(selection.commit.oid, selection.parent))
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
        { state with
            repositoryTabs =
                state.repositoryTabs
                |> List.map (fun tab ->
                    match tab.selection with
                    | Some selection when tab.id = id && selection.requestId = Some requestId ->
                        let next =
                            match result with
                            | Ok files ->
                                { selection with
                                    files = files
                                    requestId = None
                                    hasLoaded = true
                                    error = None
                                }
                            | Error error ->
                                { selection with
                                    requestId = None
                                    error = Some(string error)
                                }

                        { tab with selection = Some next }
                    | _ -> tab)
        },
        Cmd.none

    | SelectCommitFile(id, _)
    | RetryCommitDiff id ->
        match state.repositoryTabs |> List.tryFind (fun tab -> tab.id = id) with
        | Some tab ->
            match tab.selection with
            | Some selection when selection.hasLoaded && selection.requestId.IsNone ->
                let file =
                    match msg with
                    | SelectCommitFile(_, path) ->
                        if selection.diff |> Option.exists (fun diff -> diff.file.path = path) then
                            None
                        else
                            selection.files |> Array.tryFind (fun file -> file.path = path)
                    | _ ->
                        selection.diff
                        |> Option.filter (fun diff -> diff.requestId.IsNone)
                        |> Option.map (fun diff -> diff.file)

                match file with
                | Some file ->
                    let requestId = Guid.NewGuid().ToString()

                    let diff =
                        {
                            file = file
                            requestId = Some requestId
                            patch = None
                            error = None
                        }

                    let read () =
                        promise {
                            let! repository = AppTauri.Git.openRepository tab.root $"diff-{requestId}-open"

                            return!
                                AppTauri.Git.diff
                                    repository.repoId
                                    $"diff-{requestId}-patch"
                                    (GitProtocol.Commit(selection.commit.oid, selection.parent))
                                    file.path
                        }

                    { state with
                        repositoryTabs =
                            state.repositoryTabs
                            |> List.map (fun item ->
                                if item.id = id then
                                    { item with
                                        selection = Some { selection with diff = Some diff }
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

                    let cursor = if append then tab.history.nextCursor else None
                    let! page = AppTauri.Git.log repoId $"history-{requestId}-log" cursor 100
                    return repoId, page
                }

            { state with
                repositoryTabs =
                    state.repositoryTabs
                    |> List.map (fun item ->
                        if item.id = id then
                            { item with history = history }
                        else
                            item)
            },
            Cmd.OfPromise.either
                read
                ()
                (fun result -> OnRepositoryHistory(id, requestId, append, Ok result))
                (fun error -> OnRepositoryHistory(id, requestId, append, Error error))
        | _ -> state, Cmd.none

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

        { state with repositoryTabs = tabs }, Cmd.none

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
            let saveWindowStatePromise () =
                promise { do! AppTauri.Window.saveState () }

            let newState =
                { state with
                    isWindowClosePending = true
                }

            allowWindowClose <- false
            newState, Cmd.OfPromise.either saveWindowStatePromise () (fun () -> OnWindowSaveSuccess) OnWindowSaveError

    | OnWindowSaveSuccess ->
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

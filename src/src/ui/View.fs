module view

open Feliz
open System
open Feliz.DaisyUI

open projectData
open state

let pageTheme _state = theme.light

[<ReactMemoComponent>]
let private ProjectGitIndicators (check: ProjectGitCheck option) (dispatch: Msg -> unit) =
    let note text title =
        Html.span [
            prop.className "text-xs text-base-content/60"
            prop.text (text: string)
            prop.title (title: string)
        ]

    Html.div [
        prop.className "mt-auto pt-3 border-t border-base-300 flex flex-wrap items-center gap-2"
        prop.children [
            match check with
            | None -> Html.none
            | Some check when check.isLoading -> note "Checking Git…" "Checking associated repositories"
            | Some check ->
                match check.result, check.error with
                | _, Some error -> note "Git unavailable" error
                | Some result, _ when result.unsupported ->
                    note "Remote Git unavailable" "Git connections through SSH/WSL are not configured yet"
                | Some result, _ ->
                    if result.repositories.Length = 0 && result.warnings.Length = 0 then
                        note "No Git repository" "No repository found for this project"

                    for repo in result.repositories do
                        let label, color =
                            match repo.dirty with
                            | Some true -> "Uncommitted changes", "text-amber-700"
                            | Some false -> "Clean", "text-emerald-700"
                            | None -> "Status unavailable", "text-base-content/60"

                        let details =
                            [
                                Some repo.root
                                repo.branch |> Option.map (fun branch -> $"Branch: {branch}")
                                repo.error
                            ]
                            |> List.choose id
                            |> String.concat "\n"

                        Html.button [
                            prop.type' "button"
                            prop.key repo.root
                            prop.title $"Open repository\n{details}"
                            prop.ariaLabel $"Open repository {repo.name}: {label}. {details}"
                            prop.onClick (fun _ -> dispatch (OpenRepositoryTab repo))
                            prop.className
                                "flex items-center gap-1.5 rounded border border-base-300 px-2 py-1 text-xs min-w-0 hover:bg-base-300 focus-visible:outline-2 focus-visible:outline-primary cursor-pointer"
                            prop.children [
                                svgIcons.git 18
                                Html.span [ prop.className "truncate max-w-32"; prop.text repo.name ]
                                Html.span [ prop.className color; prop.text $"· {label}" ]
                            ]
                        ]

                    if result.warnings.Length > 0 then
                        note "Some paths could not be checked" (String.concat "\n" result.warnings)
                | _ -> Html.none
        ]
    ]

[<ReactMemoComponent>]
let Project (prj: ProjectData) (gitCheck: ProjectGitCheck option) (dispatch: Msg -> unit) =
    let title = sprintf "%s [%s]" prj.name prj.ide

    let remoteText =
        match prj.remote with
        | Some r ->
            match r with
            | Ssh(host, user) -> Some $"{user}@{host}"
            | Wsl distro -> Some $"WSL+{distro}"
        | None -> None

    Daisy.card [
        prop.classes [ "shadow-lg"; "p-2"; "bg-base-200"; "min-w-[350px]"; "h-full" ]
        prop.children [
            Daisy.cardBody [
                Html.div [
                    prop.classes [ "flex"; "items-start"; "justify-between"; "gap-2" ]
                    prop.children [
                        Daisy.cardTitle [ prop.className "min-w-0 break-words"; prop.text title ]
                        Html.button [
                            prop.type' "button"
                            prop.classes [
                                "btn"
                                "btn-xs"
                                "btn-square"
                                "btn-ghost"
                                "shrink-0"
                                "text-gray-400"
                                "hover:text-black"
                                "focus-visible:text-black"
                                "transition-colors"
                            ]
                            prop.title "Edit project"
                            prop.ariaLabel $"Edit project {prj.name}"
                            prop.children [ svgIcons.edit 16 ]
                            prop.onClick (fun _ -> dispatch (OpenEditProjectModal prj.id))
                        ]
                    ]
                ]
                Html.p [ prop.children [ Html.text prj.description ] ]
                match remoteText with
                | Some text -> Html.p [ prop.children [ Html.text $"Remote: {text}" ] ]
                | None -> Html.none

                Html.p [ prop.children [ Html.text prj.path ] ]
                Daisy.cardActions [
                    Daisy.button.button [
                        button.xs
                        button.outline
                        button.primary
                        prop.type' "button"
                        prop.children [ Html.text "Open" ]
                        prop.onClick (
                            React.useCallback (fun _ ->
                                FrontendLog.info $"Opening project {prj.id}"
                                dispatch (OpenProject prj.id))
                        )
                    ]
                ]
                ProjectGitIndicators gitCheck dispatch
            ]
        ]
    ]

[<ReactMemoComponent>]
let private Projects
    (projects: ProjectData list)
    (projectGit: Map<string, ProjectGitCheck>)
    (randomSalt: string)
    (dispatch: Msg -> unit)
    =
    Html.div [
        prop.classes [ "flex"; "flex-col"; "p-4"; "gap-4" ]
        prop.id $"projects-{randomSalt}"
        prop.children [
            Html.div [
                prop.id $"projects-cards-grid-{randomSalt}"
                prop.className "grid grid-cols-[repeat(auto-fill,minmax(min(100%,350px),1fr))] gap-4"
                prop.children [
                    for prj in projects do
                        yield
                            Html.div [
                                prop.key prj.id
                                prop.children [ Project prj (projectGit |> Map.tryFind prj.id) dispatch ]
                            ]
                ]
            ]
        ]
    ]

[<ReactMemoComponent>]
let private About
    (appVersion: string)
    (tauriVersion: string)
    (appDataDir: string)
    (appConfigDir: string)
    (appWindowSize: Tauri.Dpi.PhysicalSize option)
    (appWindowPosition: Tauri.Dpi.PhysicalPosition option)
    (currentTime: System.DateTime)
    (randomSalt: string)
    =
    Html.div [
        prop.classes [ "text-sm" ]
        prop.id $"about-{randomSalt}"
        prop.children [
            Html.h1 [
                prop.id "about-title"
                prop.classes [ "text-xl"; "font-semibold"; "mb-4" ]
                prop.children [ Html.text "About" ]
            ]
            Daisy.table [
                prop.classes [ "text-sm"; "table-auto"; "w-full"; "break-all"; "p-1" ]
                prop.children [
                    Html.tbody [
                        Html.tr [
                            prop.classes [ "w-fit"; "whitespace-nowrap" ]
                            prop.children [
                                Html.td [
                                    prop.classes [ "w-fit"; "whitespace-nowrap"; "p-1" ]
                                    prop.children [ Html.text "Version" ]
                                ]
                                Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text ":" ] ]
                                Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text (Version.version ()) ] ]
                            ]
                        ]
                        Html.tr [
                            Html.td [
                                prop.classes [ "w-fit"; "whitespace-nowrap"; "p-1" ]
                                prop.children [ Html.text "Tauri Ver" ]
                            ]
                            Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text ":" ] ]
                            Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text tauriVersion ] ]
                        ]
                        Html.tr [
                            Html.td [
                                prop.classes [ "w-fit"; "whitespace-nowrap"; "p-1" ]
                                prop.children [ Html.text "React Ver" ]
                            ]
                            Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text ":" ] ]
                            Html.td [
                                prop.classes [ "p-1" ]
                                prop.children [ Html.text (ReactVersion.reactVersion ()) ]
                            ]
                        ]
                        Html.tr [
                            Html.td [
                                prop.classes [ "w-fit"; "whitespace-nowrap"; "p-1" ]
                                prop.children [ Html.text "Data dir" ]
                            ]
                            Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text ":" ] ]
                            Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text appDataDir ] ]
                        ]
                        Html.tr [
                            Html.td [
                                prop.classes [ "w-fit"; "whitespace-nowrap"; "p-1" ]
                                prop.children [ Html.text "Config dir" ]
                            ]
                            Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text ":" ] ]
                            Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text appConfigDir ] ]
                        ]
                        Html.tr [
                            Html.td [
                                prop.classes [ "w-fit"; "whitespace-nowrap"; "p-1" ]
                                prop.children [ Html.text "Window size" ]
                            ]
                            Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text ":" ] ]
                            Html.td [
                                prop.classes [ "p-1" ]
                                prop.children [
                                    Html.text (
                                        match appWindowSize with
                                        | Some size -> sprintf "%.1f x %.1f" size.width size.height
                                        | None -> "N/A"
                                    )
                                ]
                            ]
                        ]
                        Html.tr [
                            Html.td [
                                prop.classes [ "w-fit"; "whitespace-nowrap"; "p-1" ]
                                prop.children [ Html.text "Window position" ]
                            ]
                            Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text ":" ] ]
                            Html.td [
                                prop.classes [ "p-1" ]
                                prop.children [
                                    Html.text (
                                        match appWindowPosition with
                                        | Some pos -> sprintf "%.1f x %.1f" pos.x pos.y
                                        | None -> "N/A"
                                    )
                                ]
                            ]
                        ]
                        Html.tr [
                            Html.td [
                                prop.classes [ "w-fit"; "whitespace-nowrap"; "p-1" ]
                                prop.children [ Html.text "Current time" ]
                            ]
                            Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text ":" ] ]
                            Html.td [
                                prop.classes [ "p-1" ]
                                prop.children [ Html.text (sprintf "%A" currentTime) ]
                            ]
                        ]
                    ]
                ]
            ]

        ]

    ]

[<ReactComponent>]
let private AboutDialog (state: State) (dispatch: Msg -> unit) =
    React.useEffectOnce (fun () ->
        let modal =
            Browser.Dom.document.getElementById "about-dialog" :?> Browser.HTMLDialogElement

        modal.showModal ()

        { new IDisposable with
            member _.Dispose() =
                if modal.``open`` then
                    modal.close ()
        })

    Html.dialog [
        prop.id "about-dialog"
        prop.className "modal"
        prop.ariaLabelledBy "about-title"
        prop.onCancel (fun event ->
            event.preventDefault ()
            dispatch CloseAbout)
        prop.children [
            Html.div [
                prop.className "modal-box w-11/12 max-w-2xl"
                prop.children [
                    About
                        state.appVersion
                        state.tauriVersion
                        state.appDataDir
                        state.appConfigDir
                        state.appWindowSize
                        state.appWindowPosition
                        state.currentTime
                        state.randomSalt
                    Html.div [
                        prop.className "modal-action"
                        prop.children [
                            Html.button [
                                prop.type' "button"
                                prop.className "btn btn-sm"
                                prop.autoFocus true
                                prop.text "Close"
                                prop.onClick (fun _ -> dispatch CloseAbout)
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

[<ReactComponent>]
let View (state: State) dispatch =
    Html.div [
        prop.className "flex flex-col h-screen overflow-hidden bg-base-100 text-base-content"
        prop.id $"app-{state.randomSalt}"
        prop.custom ("inert", state.isWindowClosePending)
        pageTheme state
        prop.children [
            DesktopTabs.TabBar state.repositoryTabs state.activeRepositoryTab dispatch
            if state.workspaceLoading then
                Html.p [
                    prop.role "status"
                    prop.className "px-4 py-2 text-sm"
                    prop.text "Restoring workspace…"
                ]
            match state.workspaceError with
            | Some error ->
                Html.div [
                    prop.role "alert"
                    prop.className "shrink-0 flex items-center gap-3 px-4 py-2 bg-error/10 text-sm"
                    prop.children [
                        Html.span [ prop.className "break-words"; prop.text error ]
                        Html.button [
                            prop.type' "button"
                            prop.className "btn btn-xs shrink-0"
                            prop.text "Retry"
                            prop.onClick (fun _ ->
                                dispatch (
                                    if state.workspaceReady then
                                        SaveWorkspace
                                    else
                                        LoadWorkspace
                                ))
                        ]
                    ]
                ]
            | None -> Html.none
            match state.projectEditor with
            | Some project ->
                Html.div [
                    prop.key project.id
                    prop.children [
                        ProjectEditor.Dialog
                            project
                            (not (state.projects |> List.exists (fun existing -> existing.id = project.id)))
                            state.isProjectSaving
                            state.projectSaveError
                            dispatch
                    ]
                ]
            | None -> Html.none
            if state.isAboutOpen then
                AboutDialog state dispatch
            Html.main [
                prop.id "main-view"
                prop.className "flex-1 min-h-0 overflow-hidden"
                prop.children [
                    Html.div [
                        prop.id "panel-projects"
                        prop.role "tabpanel"
                        prop.ariaLabelledBy "tab-projects"
                        prop.hidden state.activeRepositoryTab.IsSome
                        prop.tabIndex 0
                        prop.className "h-full overflow-auto"
                        prop.children [
                            match state.projectStorageError with
                            | Some error ->
                                Html.div [
                                    prop.role "alert"
                                    prop.className "alert alert-error m-4 whitespace-pre-wrap break-words"
                                    prop.text error
                                ]
                            | None -> Html.none
                            if List.isEmpty state.projects && state.projectStorageError.IsNone then
                                Html.div [
                                    prop.className "p-12 text-center text-base-content/60"
                                    prop.text "Add your first project using File → Add project… or Ctrl+N."
                                ]
                            Projects state.projects state.projectGit state.randomSalt dispatch
                        ]
                    ]
                    for tab in state.repositoryTabs do
                        Html.div [
                            prop.key tab.id
                            prop.id $"panel-{tab.id}"
                            prop.role "tabpanel"
                            prop.ariaLabelledBy $"tab-{tab.id}"
                            prop.hidden (state.activeRepositoryTab <> Some tab.id)
                            prop.tabIndex 0
                            prop.className "h-full overflow-auto"
                            prop.children [ DesktopTabs.RepositoryPanel tab dispatch ]
                        ]
                ]
            ]
        ]
    ]

module view

open Feliz
open Feliz.Router
open Feliz.DaisyUI

open projectData
open state

module JsInterop =
    open Fable.Core

    [<Emit("$0.showModal()")>]
    let inline showModal (_modalId: string) : unit = jsNative

    [<Emit("modal-add-project.showModal()")>]
    let inline showModalAddProject () : unit = jsNative

    [<Import("_showModalDialog", "./_ReactVersion.js")>]
    let _showModalDialog: unit -> unit = jsNative

    [<Import("_closeModalDialog", "./_ReactVersion.js")>]
    let _closeModalDialog: unit -> unit = jsNative

    [<Import("_showModalDialogById", "./_ReactVersion.js")>]
    let _showModalDialogById: string -> unit = jsNative

    [<Import("_closeModalDialogById", "./_ReactVersion.js")>]
    let _closeModalDialogById: string -> unit = jsNative


    let inline showModalDialog () = _showModalDialog ()
    let inline closeModalDialog () = _closeModalDialog ()

    let inline showModalDialogById (id: string) = _showModalDialogById id
    let inline closeModalDialogById (id: string) = _closeModalDialogById id

let pageTheme _state = theme.light

[<ReactMemoComponent>]
let private Navbar (currentUrl: string list) (randomSalt: string) =
    Daisy.navbar [
        prop.classes [ "bg-base-300"; "shadow-lg"; "justify-end" ]
        prop.id $"navbar-{randomSalt}"
        prop.children [
            Html.div [
                prop.className "flex-none"
                prop.children [
                    Daisy.menu [
                        menu.xs
                        prop.classes [ "sm:menu-horizontal" ]
                        prop.children [
                            Html.li [
                                prop.id "menu-item-project"
                                prop.children [
                                    Html.a [
                                        prop.className (
                                            if currentUrl = [] || currentUrl = [ "projects" ] then
                                                "menu-active"
                                            else
                                                ""
                                        )
                                        prop.href "#projects"
                                        prop.children [
                                            Html.span [
                                                prop.classes [ "text-sm"; "font-bold"; "font-mono" ]
                                                prop.children [ Html.text "Projects" ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                            Html.li [
                                prop.id "menu-item-about"
                                prop.children [
                                    Html.a [
                                        prop.className (if currentUrl = [ "about" ] then "menu-active" else "")
                                        prop.href "#about"
                                        prop.children [
                                            Html.span [
                                                prop.classes [ "text-sm"; "font-bold"; "font-mono" ]
                                                prop.children [ Html.text "About" ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ] // Menu
                ]
            ] // Navbar children flex-none
        ] // Navbar children
    ] // Navbar

[<ReactMemoComponent>]
let private ProjectGitIndicators (check: ProjectGitCheck option) =
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

                        Html.div [
                            prop.key repo.root
                            prop.title details
                            prop.ariaLabel $"{repo.name}: {label}. {details}"
                            prop.className
                                "flex items-center gap-1.5 rounded border border-base-300 px-2 py-1 text-xs min-w-0"
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
                                printfn "Open project: %s" prj.id
                                dispatch (OpenProject prj.id))
                        )
                    ]
                ]
                ProjectGitIndicators gitCheck
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
                prop.classes [ "grid"; "grid-cols-3"; "gap-4" ]
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
    (errors: string list)
    (randomSalt: string)
    =
    Html.div [
        prop.classes [ "p-10"; "font-mono" ]
        prop.id $"about-{randomSalt}"
        prop.children [
            Html.h1 [
                prop.classes [ "text-3xl"; "font-bold" ]
                prop.children [ Html.text "About" ]
            ]
            Daisy.table [
                prop.classes [ "text-lg"; "font-mono"; "table-auto"; "w-fit"; "whitespace-nowrap"; "p-1" ]
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

            Daisy.divider []
            Html.div [
                prop.children [
                    for e in errors do
                        yield Html.p [ Html.text (sprintf "%s" e) ]
                ]
            ]
        ]

    ]

[<ReactMemoComponent>]
let private Page404 (currentUrl: string list) (randomSalt: string) =

    let p = sprintf "Page `%A` not found." currentUrl

    Html.div [
        prop.className "p-10"
        prop.id $"404-{randomSalt}"
        prop.children [
            Html.h1 [ prop.className "text-4xl font-bold"; prop.children [ Html.text "404" ] ]
            Html.p [ prop.className "text-lg"; prop.children [ Html.text p ] ]
        ]
    ]

[<ReactComponent>]
let View state dispatch =
    let page =
        Html.div [
            prop.classes [
                "flex"
                "flex-col"
                "h-screen"
                "overflow-hidden"
                "bg-base-100"
                "text-base-content"
                "font-mono"
            ]
            prop.id $"app-{state.randomSalt}"
            pageTheme state // Apply the theme
            prop.children [
                Navbar state.currentUrl state.randomSalt
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
                Html.div [
                    prop.id "main-view"
                    prop.classes [ "mt-0"; "overflow-y-auto"; "h-screen" ]
                    prop.children [
                        match state.projectStorageError with
                        | Some error ->
                            Html.div [
                                prop.role "alert"
                                prop.classes [ "alert"; "alert-error"; "m-4"; "whitespace-pre-wrap"; "break-words" ]
                                prop.text error
                            ]
                        | None -> Html.none

                        match state.currentUrl with
                        | [] -> Projects state.projects state.projectGit state.randomSalt dispatch
                        | [ "projects" ] -> Projects state.projects state.projectGit state.randomSalt dispatch
                        | [ "about" ] ->
                            About
                                state.appVersion
                                state.tauriVersion
                                state.appDataDir
                                state.appConfigDir
                                state.appWindowSize
                                state.appWindowPosition
                                state.currentTime
                                state.errors
                                state.randomSalt
                        | _ -> Page404 state.currentUrl state.randomSalt
                    ]
                ]
            ]
        ]

    React.router [ router.onUrlChanged (UrlChanged >> dispatch); router.children [ page ] ]

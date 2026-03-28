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
let private Navbar (currentUrl: string list) (randomSalt: string) (dispatch: Msg -> unit) =
    Daisy.navbar [
        prop.classes [ "bg-base-300"; "shadow-lg" ]
        prop.id $"navbar-{randomSalt}"
        prop.children [
            Html.div [
                prop.className "flex-1"
                prop.children [
                    Daisy.button.button [
                        prop.classes [ "text-sm" ]
                        button.primary
                        button.sm
                        prop.text "Add project"
                        prop.id $"btn-add-project-{randomSalt}"
                        prop.type' "button"
                        prop.onClick (React.useCallback (fun _ -> dispatch OpenAddProjectModal))
                    ]
                ]
            ]
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
let Project (prj: ProjectData) (dispatch: Msg -> unit) =
    let title = sprintf "%s [%s]" prj.name prj.ide

    let remoteText =
        match prj.remote with
        | Some r ->
            match r with
            | Ssh(host, user) -> Some $"{user}@{host}"
            | Wsl distro -> Some $"WSL+{distro}"
        | None -> None

    Daisy.card [
        prop.classes [ "shadow-lg"; "p-2"; "bg-base-200"; "min-w-[350px]" ]
        prop.children [
            Daisy.cardBody [
                Daisy.cardTitle [ prop.children [ Html.text title ] ]
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
            ]
        ]
    ]

[<ReactMemoComponent>]
let private Projects (projects: ProjectData list) (randomSalt: string) (dispatch: Msg -> unit) =
    Html.div [
        prop.classes [ "flex"; "flex-col"; "p-4"; "gap-4" ]
        prop.id $"projects-{randomSalt}"
        prop.children [
            Html.div [
                prop.id $"projects-cards-grid-{randomSalt}"
                prop.classes [ "grid"; "grid-cols-3"; "gap-4" ]
                prop.children [
                    for prj in projects do
                        yield Project prj dispatch
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
                                Html.td [ prop.classes [ "p-1" ]; prop.children [ Html.text appVersion ] ]
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
let private ModalAddProject
    (randomSalt: string)
    (isAddProjectModalOpen: bool)
    (formAddProjectName: string)
    (formAddProjectDescription: string)
    (formAddProjectPath: string)
    (dispatch: Msg -> unit)
    =
    let modalId = $"modal-add-project-{randomSalt}"
    let remoteType, setRemoteType = React.useState ""
    let remoteHost, setRemoteHost = React.useState ""
    let remoteUsername, setRemoteUsername = React.useState ""
    let remoteWslDistro, setRemoteWslDistro = React.useState ""
    let ideValue, setIdeValue = React.useState "vscode"

    React.useEffect (
        (fun () ->
            if isAddProjectModalOpen then
                setRemoteType ""
                setRemoteHost ""
                setRemoteUsername ""
                setRemoteWslDistro ""

            let modal =
                Browser.Dom.document.getElementById modalId :?> Browser.HTMLDialogElement

            if modal <> null then
                modal.onclose <- (fun _ -> dispatch CloseAddProjectModal)

                if isAddProjectModalOpen then
                    if not modal.``open`` then
                        modal.showModal ()
                elif modal.``open`` then
                    modal.close ()),
        [| box modalId; box isAddProjectModalOpen |]
    )

    Html.dialog [
        prop.id modalId
        prop.classes [ "modal"; "active" ]
        prop.children [
            Html.div [
                prop.classes [ "modal-box" ]
                prop.children [
                    Html.form [
                        prop.id "form-add-project-close"
                        prop.method "dialog"
                        prop.children [
                            Html.button [
                                prop.type' "button"
                                prop.classes [
                                    "btn"
                                    "btn-xs"
                                    "btn-square"
                                    "btn-ghost"
                                    "absolute"
                                    "top-2"
                                    "right-2"
                                ]
                                prop.children [ Html.text "✕" ]
                                prop.onClick (React.useCallback (fun _ -> dispatch CloseAddProjectModal))
                            ]
                        ]
                    ]
                    Html.h3 [ prop.children [ Html.text "Add project" ] ]
                    Html.form [
                        prop.id "form-add-project"
                        prop.children [
                            Daisy.fieldset [
                                Daisy.fieldsetLabel "Name"
                                Daisy.input [
                                    input.sm
                                    prop.id "form-add-project-name"
                                    prop.placeholder "Project name"
                                    prop.value formAddProjectName
                                    prop.required true
                                    prop.onChange (
                                        React.useCallback (fun newValue ->
                                            dispatch (FormAddProjectNameChanged newValue))
                                    )
                                ]
                                Daisy.fieldsetLabel "Description"
                                Daisy.input [
                                    prop.id "form-add-project-description"
                                    input.sm
                                    prop.placeholder "Project description"
                                    prop.value formAddProjectDescription
                                    prop.onChange (
                                        React.useCallback (fun newValue ->
                                            dispatch (FormAddProjectDescriptionChanged newValue))
                                    )
                                ]

                                Daisy.fieldsetLabel "IDE"
                                Daisy.select [
                                    select.sm

                                    prop.id "form-add-project-ide"
                                    prop.value ideValue
                                    prop.onChange (
                                        React.useCallback (fun newValue ->
                                            if not (newValue = "vscode") then
                                                setRemoteType "" // reset remote type if IDE is not vscode, since currently only vscode supports remote projects

                                            setIdeValue newValue)
                                    )
                                    prop.children [
                                        Html.option [ prop.value "vscode"; prop.children [ Html.text "VSCode" ] ]
                                        Html.option [
                                            prop.value "visualstudio2022"
                                            prop.children [ Html.text "Visual Studio 2022" ]
                                        ]
                                        Html.option [
                                            prop.value "visualstudio2026"
                                            prop.children [ Html.text "Visual Studio 2026" ]
                                        ]
                                        Html.option [ prop.value "rider"; prop.children [ Html.text "Rider" ] ]

                                    ]
                                ]

                                Daisy.fieldsetLabel "Remote"
                                Daisy.select [
                                    select.sm
                                    prop.id "form-add-project-remote-type"
                                    prop.value remoteType
                                    prop.disabled (ideValue <> "vscode") // currently only vscode supports remote projects
                                    prop.children [
                                        Html.option [ prop.value ""; prop.children [ Html.text "None" ] ]
                                        Html.option [ prop.value "ssh"; prop.children [ Html.text "SSH" ] ]
                                        Html.option [ prop.value "wsl"; prop.children [ Html.text "WSL" ] ]
                                    ]
                                    prop.onChange (React.useCallback (fun newValue -> setRemoteType newValue))
                                ]
                                Html.div [
                                    prop.classes [
                                        if not (remoteType = "wsl") then
                                            "hidden"
                                    ]
                                    prop.children [
                                        Daisy.fieldsetLabel "WSL distro"
                                        Daisy.input [
                                            input.sm
                                            prop.id "form-add-project-remote-wsl-distro"
                                            prop.placeholder "Ubuntu"
                                            prop.value remoteWslDistro
                                            prop.required true
                                            prop.onChange (
                                                React.useCallback (fun newValue -> setRemoteWslDistro newValue)
                                            )
                                        ]
                                    ]
                                ]

                                Html.div [
                                    prop.classes [
                                        if not (remoteType = "ssh") then
                                            "hidden"
                                    ]
                                    prop.children [
                                        Daisy.fieldsetLabel "SSH"
                                        Html.div [
                                            prop.classes [ "flex"; "gap-2" ]
                                            prop.children [
                                                Daisy.input [
                                                    input.sm
                                                    prop.id "form-add-project-remote-host"
                                                    prop.classes [ "flex-1" ]
                                                    prop.placeholder "Host"
                                                    prop.value remoteHost
                                                    prop.required true
                                                    prop.onChange (
                                                        React.useCallback (fun newValue -> setRemoteHost newValue)
                                                    )
                                                ]
                                                Daisy.input [
                                                    input.sm
                                                    prop.id "form-add-project-remote-username"
                                                    prop.classes [ "flex-1" ]
                                                    prop.placeholder "Username"
                                                    prop.value remoteUsername
                                                    prop.required true
                                                    prop.onChange (
                                                        React.useCallback (fun newValue -> setRemoteUsername newValue)
                                                    )
                                                ]
                                            ]
                                        ]
                                    ]
                                ]

                                Daisy.fieldsetLabel "Solution/Workspace"
                                Daisy.input [
                                    prop.id "form-add-project-file"
                                    input.sm
                                    prop.placeholder "Solution/Workspace file"
                                    prop.value formAddProjectPath
                                    prop.onChange (
                                        React.useCallback (fun newValue ->
                                            dispatch (FormAddProjectPathChanged newValue))
                                    )
                                ]
                            ]
                        ]
                    ]
                    Html.div [
                        prop.classes [ "modal-action" ]
                        prop.children [
                            Daisy.button.label [
                                button.primary
                                button.sm
                                prop.text "Accept"
                                prop.onClick (
                                    React.useCallback (fun _ ->
                                        printfn "Accept"
                                        //get form values
                                        let nameEl =
                                            Browser.Dom.document.getElementById "form-add-project-name"
                                            :?> Browser.Types.HTMLInputElement

                                        let descriptionEl =
                                            Browser.Dom.document.getElementById "form-add-project-description"
                                            :?> Browser.Types.HTMLInputElement

                                        let fileEl =
                                            Browser.Dom.document.getElementById "form-add-project-file"
                                            :?> Browser.Types.HTMLInputElement

                                        let ideEl =
                                            Browser.Dom.document.getElementById "form-add-project-ide"
                                            :?> Browser.Types.HTMLSelectElement

                                        let name = nameEl.value
                                        let description = descriptionEl.value
                                        let file = fileEl.value
                                        let ide = ideEl.value

                                        let remote =
                                            match remoteType with
                                            | "ssh" when
                                                not (System.String.IsNullOrWhiteSpace remoteHost)
                                                && not (System.String.IsNullOrWhiteSpace remoteUsername)
                                                ->
                                                Some(Ssh(remoteHost, remoteUsername))
                                            | "wsl" when not (System.String.IsNullOrWhiteSpace remoteWslDistro) ->
                                                Some(Wsl remoteWslDistro)
                                            | _ -> None

                                        let pd =
                                            { id = System.Guid.NewGuid().ToString()
                                              name = name
                                              lastOpened = System.DateTime.Now
                                              description = description
                                              path = file
                                              ide = ide
                                              environment = Map.empty
                                              remote = remote }

                                        let file_name = sprintf "%s-%s.%s" pd.name pd.id "json"
                                        let pd_json = pd |> ProjectData.toJson

                                        dispatch CloseAddProjectModal
                                        dispatch (AddOrUpdateProject(file_name, pd_json)))
                                )
                            ]
                            Daisy.button.label [
                                button.primary
                                button.sm
                                prop.text "Cancel"
                                prop.onClick (fun _ ->
                                    printfn "Cancel"
                                    dispatch CloseAddProjectModal)
                            ]
                        ]
                    ]
                ]
            ]
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
                Navbar state.currentUrl state.randomSalt dispatch
                ModalAddProject
                    state.randomSalt
                    state.isAddProjectModalOpen
                    state.formAddProjectName
                    state.formAddProjectDescription
                    state.formAddProjectPath
                    dispatch
                Html.div [
                    prop.id "main-view"
                    prop.classes [ "mt-0"; "overflow-y-auto"; "h-screen" ]
                    prop.children [
                        match state.currentUrl with
                        | [] -> Projects state.projects state.randomSalt dispatch
                        | [ "projects" ] -> Projects state.projects state.randomSalt dispatch
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

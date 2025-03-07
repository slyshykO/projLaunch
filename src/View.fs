module view

open Feliz
open Feliz.Router
open Feliz.DaisyUI

open state

module JsInterop =
    open Fable.Core

    [<Emit("$0.showModal()")>]
    let inline showModal (modalId: string) : unit = jsNative

    [<Emit("modal-add-project.showModal()")>]
    let inline showModalAddProject () : unit = jsNative

    [<Import("_showModalDialog", "./_ReactVersion.js")>]
    let _showModalDialog: unit -> unit = jsNative

    [<Import("_closeModalDialog", "./_ReactVersion.js")>]
    let _closeModalDialog: unit -> unit = jsNative

    let inline showModalDialog () = _showModalDialog ()
    let inline closeModalDialog () = _closeModalDialog ()

let pageTheme state =
    state |> ignore
    theme.light

let private navbar state _dispatch =
    Daisy.navbar [
        prop.classes [ "bg-base-300"; "shadow-lg" ]
        prop.id "navbar"
        prop.key "navbar"
        prop.children [
            //Html.div [ prop.className "flex-1"; prop.children [ Html.text state.value ] ]
            Html.div [
                prop.className "flex-1"
                prop.children [
                    Daisy.button.button [
                        button.primary
                        button.sm
                        prop.text "Add project"
                        prop.id "btn-add-project"
                        prop.type' "button"
                        prop.onClick (fun _ -> JsInterop.showModalDialog ())
                    ]
                ]
            ]
            Html.div [
                prop.className "flex-none"
                prop.children [
                    // Daisy.button.button [
                    //     prop.classes [ "btn"; "btn-square"; "btn-ghost" ]
                    //     prop.children [ Html.text "greet" ]
                    //     prop.onClick (fun _ -> dispatch (Greet "Alex"))
                    // ]
                    Daisy.menu [
                        menu.horizontal
                        menu.sm
                        prop.children [
                            Html.li [
                                prop.id "menu-item-project"
                                prop.key "menu-item-project"
                                prop.children [
                                    Html.a [
                                        prop.className (
                                            if state.currentUrl = [] || state.currentUrl = [ "projects" ] then
                                                "active"
                                            else
                                                ""
                                        )
                                        prop.href "#projects"
                                        prop.children [ Html.text "Projects" ]
                                    ]
                                ]
                            ]
                            Html.li [
                                prop.id "menu-item-about"
                                prop.key "menu-item-about"
                                prop.children [
                                    Html.a [
                                        prop.className (if state.currentUrl = [ "about" ] then "active" else "")
                                        prop.href "#about"
                                        prop.children [ Html.text "About" ]
                                    ]
                                ]
                            ]
                        ]
                    ] // Menu
                ]
            ] // Navbar children flex-none
        ] // Navbar children
    ] // Navbar


let project (prj: ProjectData) dispatch =
    let title = sprintf "%s [%s]" prj.name prj.ide

    Daisy.card [
        prop.classes [ "shadow-lg"; "p-2"; "bg-base-200"; "min-w-[350px]" ]
        prop.children [
            Daisy.cardBody [
                Daisy.cardTitle [ prop.children [ Html.text title ] ]
                Html.p [ prop.children [ Html.text prj.description ] ]
                Html.p [ prop.children [ Html.text prj.path ] ]
                Daisy.cardActions [
                    Daisy.button.button [
                        prop.classes [ "btn-xs"; "btn-outline" ]
                        prop.children [ Html.text "Open" ]
                        prop.onClick (fun _ ->
                            printfn "Open project: %s" prj.id
                            dispatch (OpenProject prj.id))
                    ]
                ]
            ]
        ]
    ]

let private projects state dispatch =
    Html.div [
        prop.classes [ "flex"; "flex-col"; "p-4"; "gap-4" ]

        prop.id "projects"
        prop.children [
            Html.div [
                prop.id "projects-cards-grid"
                prop.classes [ "grid"; "grid-cols-3"; "gap-4" ]
                prop.children [
                    for prj in state.projects do
                        yield project prj dispatch
                ]
            ]
        ]
    ]

let private about state =
    Html.div [
        prop.className "p-10"
        prop.id "about"
        prop.children [
            Html.h1 [ prop.className "text-4xl font-bold"; prop.children [ Html.text "About" ] ]
            Html.p [
                prop.className "text-lg"
                prop.children [ Html.text (sprintf "Version   : %s" state.appVersion) ]
            ]
            Html.p [
                prop.className "text-lg"
                prop.children [ Html.text (sprintf "Data dir  : %s" state.appDataDir) ]
            ]
            Html.p [
                prop.className "text-lg"
                prop.children [ Html.text (sprintf "Config dir: %s" state.appConfigDir) ]
            ]
            Daisy.divider []
            Html.div [
                prop.children [
                    for e in state.errors do
                        yield Html.p [ Html.text (sprintf "%s" e) ]
                ]
            ]
        ]

    ]


let private page404 state =

    let p = sprintf "Page `%A` not found." state.currentUrl

    Html.div [
        prop.className "p-10"
        prop.id "404"
        prop.children [
            Html.h1 [ prop.className "text-4xl font-bold"; prop.children [ Html.text "404" ] ]
            Html.p [ prop.className "text-lg"; prop.children [ Html.text p ] ]
        ]
    ]

let private modalAddProject state dispatch =
    Html.dialog [
        prop.id "modal-add-project"
        prop.key "modal-add-project"
        prop.classes [ "modal"; "active" ]
        prop.children [
            Html.div [
                prop.classes [ "modal-box" ]
                prop.children [
                    Html.form [
                        prop.method "dialog"
                        prop.children [
                            Html.button [
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
                                    prop.value state.formAddProjectName
                                    prop.required true
                                    prop.onChange (fun newValue -> dispatch (FormAddProjectNameChanged newValue))
                                ]
                                Daisy.fieldsetLabel "Description"
                                Daisy.input [
                                    prop.id "form-add-project-description"
                                    input.sm
                                    prop.placeholder "Project description"
                                    prop.value state.formAddProjectDescription
                                    prop.onChange (fun newValue -> dispatch (FormAddProjectDescriptionChanged newValue))
                                ]
                                Daisy.fieldsetLabel "Solution/Workspace"
                                Daisy.input [
                                    prop.id "form-add-project-file"
                                    input.sm
                                    prop.placeholder "Solution/Workspace file"
                                    prop.value state.formAddProjectPath
                                    prop.onChange (fun newValue -> dispatch (FormAddProjectPathChanged newValue))
                                ]
                                Daisy.fieldsetLabel "IDE"
                                Daisy.select [
                                    select.sm
                                    prop.id "form-add-project-ide"
                                    prop.children [
                                        Html.option [ prop.value "vscode"; prop.children [ Html.text "VSCode" ] ]
                                        Html.option [
                                            prop.value "visualstudio2022"
                                            prop.children [ Html.text "Visual Studio 2022" ]
                                        ]
                                    ]
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
                                prop.onClick (fun _ ->
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

                                    let pd =
                                        { id = System.Guid.NewGuid().ToString()
                                          name = name
                                          lastOpened = System.DateTime.Now
                                          description = description
                                          path = file
                                          ide = ide
                                          environment = Map.empty }

                                    let file_name = sprintf "%s-%s.%s" pd.name pd.id "json"
                                    let pd_json = pd |> ProjectData.toJson

                                    JsInterop.closeModalDialog ()
                                    dispatch (AddOrUpdateProject(file_name, pd_json)))
                            ]
                            Daisy.button.label [
                                button.primary
                                button.sm
                                prop.text "Cancel"
                                prop.onClick (fun _ ->
                                    printfn "Cancel"
                                    JsInterop.closeModalDialog ())
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let view state dispatch =
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
            prop.id "app"
            prop.key "app"
            pageTheme state // Apply the theme
            prop.children [
                navbar state dispatch
                modalAddProject state dispatch
                Html.div [
                    prop.key "page-internal-div"
                    prop.classes [ "mt-0"; "overflow-y-auto"; "h-screen" ]
                    prop.children [
                        match state.currentUrl with
                        | [] -> projects state dispatch
                        | [ "projects" ] -> projects state dispatch
                        | [ "about" ] -> about state
                        | _ -> page404 state
                    ]
                ]
            ]
        ]

    React.router [ router.onUrlChanged (UrlChanged >> dispatch); router.children [ page ] ]

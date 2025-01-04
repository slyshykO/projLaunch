module view

open Feliz
open Feliz.Router
open Feliz.DaisyUI

open state
open svgIcons

let pageTheme state =
    state |> ignore
    theme.light

let private navbar state dispatch =
    Daisy.navbar [
        prop.classes [ "bg-base-300"; "shadow-lg" ]
        prop.id "navbar"
        prop.children [
            //Html.div [ prop.className "flex-1"; prop.children [ Html.text state.value ] ]
            Html.div [ prop.className "flex-1" ]
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
        prop.classes [ "shadow-lg"; "p-2"; "bg-base-200" ]
        prop.children [
            Daisy.cardBody [
                Daisy.cardTitle [ prop.children [ Html.text title ] ]
                Html.p [ prop.children [ Html.text prj.description ] ]
                Html.p [ prop.children [ Html.text prj.path ] ]
                Daisy.cardActions [
                    Daisy.button.button [
                        prop.classes [ "btn-xs"; "btn-outline" ]
                        prop.children [ Html.text "Open" ]
                        prop.onClick (fun _ -> printfn "Open project: %s" prj.path)
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
        prop.key "about"
        prop.children [
            Html.h1 [ prop.className "text-4xl font-bold"; prop.children [ Html.text "About" ] ]
            Html.p [
                prop.className "text-lg"
                prop.children [ Html.text "This is the about page" ]
            ]
            Html.p [
                prop.className "text-lg"
                prop.children [ Html.text (sprintf "Data dir: %s" state.dataDir) ]
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
            pageTheme state // Apply the theme
            prop.children [
                navbar state dispatch
                Html.div [
                    prop.classes [ "mt-0"; "overflow-y-auto"; "h-screen" ]
                    prop.children [
                        match state.currentUrl with
                        | []
                        | [ "projects" ] -> projects state dispatch
                        | [ "about" ] -> about state
                        | _ -> page404 state
                    ]
                ]
            ]
        ]

    React.router [ router.onUrlChanged (UrlChanged >> dispatch); router.children [ page ] ]

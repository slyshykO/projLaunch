module view

open Feliz
open Feliz.Router
open Feliz.DaisyUI

open state
open svgIcons

let pageTheme state =
    state |> ignore
    theme.light

let footer () =
    Daisy.footer [
        prop.id "footer"
        prop.className "p-2 text-base-content flex flex-row"
        prop.children [
            Html.aside [
                prop.className "w-full align-center"
                prop.children [
                    Html.a [
                        prop.className "link link-hover link-primary text-lg font-bold"
                        prop.href "https://efps.com/"
                        prop.text "© 2024, EFPS, Inc."
                    ]
                ]
            ]
            Html.div [
                prop.className "w-full justify-end min-h-10"
                prop.children [
                    Html.a [
                        prop.className "link link-hover link-primary text-lg font-bold"
                        prop.href "https://efps.com/"
                        prop.children [ Html.span [ prop.children [ bulbElectricEnergySvg 32 ] ] ]
                    ]
                ]
            ]
        ]
    ]

let view state dispatch =
    let page =
        Html.div [
            prop.className "font-mono bg-base-100 text-base-content h-screen"
            pageTheme state // Apply the theme
            prop.children [
                Daisy.drawer [
                    prop.id "drawer"
                    prop.className "lg:drawer-open"
                    prop.children [
                        Daisy.drawerToggle [
                            prop.id "drawer-toggle"
                            prop.label "Open drawer"
                            prop.title "Open drawer"
                        ]
                        Daisy.drawerContent [
                            prop.className "flex flex-col h-full justify-center"
                            prop.children [
                                // navbar here
                                Html.div [ // content
                                    prop.className "max-w-screen-lg mx-auto"
                                    prop.id "content"
                                    prop.children [
                                        Html.h1 [ prop.text "Drawer" ]
                                        Html.p [ prop.text "This is a drawer component" ]
                                    ]
                                ]
                                Html.div [ prop.className "flex-grow" ]
                                Html.div [ prop.className "w-full h-0.5 bg-gray-400" ]
                                footer ()
                            ]
                        ]
                        Daisy.drawerSide [
                            prop.children [
                                Daisy.drawerOverlay [ prop.htmlFor "drawer-toggle" ]
                                Html.div [
                                    prop.className "p-4"
                                    prop.children [
                                        Html.h1 [ prop.text "Drawer" ]
                                        Html.p [ prop.text "This is a drawer overlay" ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

    React.router [ router.onUrlChanged (UrlChanged >> dispatch); router.children [ page ] ]

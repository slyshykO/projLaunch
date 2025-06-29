module Main

// import *css
//Fable.Core.JsInterop.importAll "./index.css"

open Elmish
open Elmish.React
open Fable.Core.JsInterop
open Fable.Core
open System

open state
open view

let appWindow = Tauri.Window.Window.getCurrent ()

let onCloseRequested () =

    let start dispatch =
        // This is where you can handle the close request
        // For example, you can dispatch a message to save state or confirm closing
        appWindow?on ("close-requested", fun _ -> dispatch WindowSave)

        { new IDisposable with
            member _.Dispose() = appWindow?off ("close-requested") }

    start

let onWindowResized () =
    let start dispatch =
        // This is where you can handle the window resize event
        // For example, you can dispatch a message to update the state with the new size
        appWindow?on ("resized", fun _ -> dispatch WindowResized)

        { new IDisposable with
            member _.Dispose() = appWindow?off ("resized") }

    start

let timer onTick =
    let start dispatch =
        let intervalId = JS.setInterval (fun _ -> dispatch (onTick DateTime.Now)) 1000

        { new IDisposable with
            member _.Dispose() = JS.clearInterval intervalId }

    start

// let subscriptions model : Sub<Msg> =
//     [ [ "timer" ], timer Tick
//       [ "window-resized" ], onWindowResized ()
//       [ "close-requested" ], onCloseRequested () ]

let subscriptions model : Sub<Msg> = [ [ "timer" ], timer Tick ]

Program.mkProgram init update view
|> Program.withSubscription subscriptions
|> Program.withReactSynchronous "feliz-app-364e6a85-5c8c-4f74-a6c4-470c3700aadb"
|> Program.run

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
        let mutable unsubscribe: Tauri.Event.UnlistenFn option = None

        let closeRequestedHandler (event: Tauri.Window.CloseRequestedEvent) =
            if allowWindowClose then
                printfn "Close requested after save, allowing window to close"
                allowWindowClose <- false
            else
                printfn "Close requested, dispatching WindowSave message"
                event.preventDefault ()
                dispatch WindowSave

        promise {
            let! unlisten = appWindow.onCloseRequested closeRequestedHandler
            unsubscribe <- Some unlisten
        }
        |> ignore

        { new IDisposable with
            member _.Dispose() =
                unsubscribe |> Option.iter (fun unlisten -> unlisten ()) }

    start

let onWindowResized () =
    let start dispatch =
        let mutable unsubscribe: Tauri.Event.UnlistenFn option = None

        let resizeHandler (event: Tauri.Event.Event<Tauri.Dpi.PhysicalSize>) =
            printfn "Window resized, dispatching WindowResized message"
            dispatch (WindowResized event.payload)

        promise {
            let! unlisten = appWindow.onResized resizeHandler
            unsubscribe <- Some unlisten
        }
        |> ignore

        { new IDisposable with
            member _.Dispose() =
                printfn "Unsubscribing from window resized event"
                unsubscribe |> Option.iter (fun unlisten -> unlisten ()) }

    start

let timer onTick =
    let start dispatch =
        let intervalId = JS.setInterval (fun _ -> dispatch (onTick DateTime.Now)) 1000

        { new IDisposable with
            member _.Dispose() = JS.clearInterval intervalId }

    start

let subscriptions model : Sub<Msg> =
    [ [ "timer" ], timer Tick
      [ "window-resized" ], onWindowResized ()
      [ "close-requested" ], onCloseRequested () ]

// let subscriptions model : Sub<Msg> = [ [ "timer" ], timer Tick ]

Program.mkProgram init update View
|> Program.withSubscription subscriptions
|> Program.withReactSynchronous "feliz-app-364e6a85-5c8c-4f74-a6c4-470c3700aadb"
|> Program.run



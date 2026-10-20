module Main

// import *css
//Fable.Core.JsInterop.importAll "../index.css"

open Elmish
open Elmish.React
open Fable.Core
open System

open state
open view

let onCloseRequested () =

    let start dispatch =
        let mutable unsubscribe: Tauri.Event.UnlistenFn option = None

        let closeRequestedHandler (event: Tauri.Window.CloseRequestedEvent) =
            if allowWindowClose then
                allowWindowClose <- false
            else
                FrontendLog.debug "Close requested; saving window state"
                event.preventDefault ()
                dispatch WindowSave

        promise {
            let! unlisten = AppTauri.Window.onCloseRequested closeRequestedHandler
            unsubscribe <- Some unlisten
        }
        |> ignore

        { new IDisposable with
            member _.Dispose() =
                unsubscribe |> Option.iter (fun unlisten -> unlisten ())
        }

    start

let onWindowResized () =
    let start dispatch =
        let mutable unsubscribe: Tauri.Event.UnlistenFn option = None

        let resizeHandler (size: Tauri.Dpi.PhysicalSize) = dispatch (WindowResized size)

        promise {
            let! unlisten = AppTauri.Window.onResized resizeHandler
            unsubscribe <- Some unlisten
        }
        |> ignore

        { new IDisposable with
            member _.Dispose() =
                unsubscribe |> Option.iter (fun unlisten -> unlisten ())
        }

    start

let onWindowMoved () =
    let start dispatch =
        let mutable unsubscribe: Tauri.Event.UnlistenFn option = None

        let moveHandler (position: Tauri.Dpi.PhysicalPosition) = dispatch (WindowMoved position)

        promise {
            let! unlisten = AppTauri.Window.onMoved moveHandler
            unsubscribe <- Some unlisten
        }
        |> ignore

        { new IDisposable with
            member _.Dispose() =
                unsubscribe |> Option.iter (fun unlisten -> unlisten ())
        }

    start

let timer onTick =
    let start dispatch =
        let intervalId = JS.setInterval (fun _ -> dispatch (onTick DateTime.Now)) 1000

        { new IDisposable with
            member _.Dispose() = JS.clearInterval intervalId
        }

    start

let subscriptions model : Sub<Msg> =
    [
        [ "frontend-logging" ], (fun _ -> FrontendLog.start ())
        [ "timer" ], timer Tick
        if AppTauri.Core.isAvailable () then
            [ "menu-add-project" ], ProjectMenu.onAddProject ()
            [ "menu-actions" ], ProjectMenu.onActions ()
        [ "window-resized" ], onWindowResized ()
        [ "window-moved" ], onWindowMoved ()
        [ "close-requested" ], onCloseRequested ()
        [ "git-status-focus" ],
        (fun dispatch ->
            let handler (_: Browser.Types.Event) = dispatch RefreshGitStatuses
            Browser.Dom.window.addEventListener ("focus", handler)

            { new IDisposable with
                member _.Dispose() =
                    Browser.Dom.window.removeEventListener ("focus", handler)
            })
    ]


Program.mkProgram init update View
|> Program.withErrorHandler (fun (_message, error) -> FrontendLog.error $"Unhandled Elmish error: {error}")
|> Program.withSubscription subscriptions
|> Program.withReactSynchronous "feliz-app-364e6a85-5c8c-4f74-a6c4-470c3700aadb"
|> Program.run

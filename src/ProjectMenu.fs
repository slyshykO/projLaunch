module ProjectMenu

open System
open Fable.Core
open state

[<Emit("$0.isComposing === true")>]
let private isComposing (_event: Browser.Types.KeyboardEvent) : bool = jsNative

[<Emit("/^Mac/i.test(navigator.platform)")>]
let private isMacPlatform () : bool = jsNative

let onAddProject () =
    let start dispatch =
        let mutable disposed = false
        let mutable unsubscribe: Tauri.Event.UnlistenFn option = None

        // WebView2 can consume native menu accelerators before Tauri sees them.
        // Use the physical key so Ctrl+N also works with Cyrillic layouts.
        let isMac = isMacPlatform ()
        let onKeyDown (rawEvent: Browser.Types.Event) =
            let event = rawEvent :?> Browser.Types.KeyboardEvent
            let commandPressed =
                if isMac then event.metaKey && not event.ctrlKey
                else event.ctrlKey && not event.metaKey

            if commandPressed && event.code = "KeyN" && not event.altKey && not event.shiftKey && not (isComposing event) then
                event.preventDefault ()
                if not event.repeat && not disposed then dispatch OpenAddProjectModal

        Browser.Dom.window.addEventListener("keydown", onKeyDown, true)

        promise {
            try
                let! unlisten = AppTauri.Window.onAddProject (fun () ->
                    if not disposed then dispatch OpenAddProjectModal)
                if disposed then unlisten () else unsubscribe <- Some unlisten
            with error ->
                Browser.Dom.console.error("Could not subscribe to the Add project menu", error)
        }
        |> ignore

        { new IDisposable with
            member _.Dispose() =
                disposed <- true
                Browser.Dom.window.removeEventListener("keydown", onKeyDown, true)
                unsubscribe |> Option.iter (fun unlisten -> unlisten ()) }

    start


module AppTauri

open Fable.Core
open Fable.Core.JsInterop

module Core =

    let isAvailable () = Tauri.Tauri.Tauri.isTauri ()

    let invoke<'T> (command: string) (args: obj option) : JS.Promise<'T> =
        Tauri.Tauri.Tauri.invoke (command, args)

    let greet (name: string) : JS.Promise<string> =
        invoke "greet" (Some(createObj [ "name" ==> name ]))

    let getDataDir () : JS.Promise<string> =
        Tauri.Tauri.Tauri.appDataDir ()

    let getConfigDir () : JS.Promise<string> =
        Tauri.Tauri.Tauri.appConfigDir ()

    let getAppVersion () : JS.Promise<string> =
        Tauri.Tauri.Tauri.getVersion ()

    let getTauriVersion () : JS.Promise<string> =
        Tauri.Tauri.Tauri.getTauriVersion ()

module Projects =

    let getProjects () : JS.Promise<string array> =
        Core.invoke "get_projects" None

    /// The delay is frontend-only and is useful for app startup sequencing.
    let getProjectsAfterStartup (startupDelayMs: int) : JS.Promise<string array> =
        promise {
            if startupDelayMs > 0 then
                do! Promise.sleep startupDelayMs

            return! getProjects ()
        }

    let openProject (id: string) : JS.Promise<unit> =
        Core.invoke "open_project" (Some(createObj [ "id" ==> id ]))

    let rewriteProjectFile (id: string) (content: string) : JS.Promise<unit> =
        Core.invoke "rewrite_project_file" (Some(createObj [ "id" ==> id; "content" ==> content ]))

module Window =

    let current () =
        Tauri.Window.Window.getCurrent ()

    let saveState () : JS.Promise<unit> =
        Tauri.WindowState.saveWindowState Tauri.WindowState.StateFlags.ALL

    let closeCurrent () : JS.Promise<unit> =
        current().close ()

    let onResized (handler: Tauri.Dpi.PhysicalSize -> unit) : JS.Promise<Tauri.Event.UnlistenFn> =
        current().onResized(fun e -> handler e.payload)

    let onMoved (handler: Tauri.Dpi.PhysicalPosition -> unit) : JS.Promise<Tauri.Event.UnlistenFn> =
        current().onMoved(fun e -> handler e.payload)

    let onCloseRequested (handler: Tauri.Window.CloseRequestedEvent -> unit) : JS.Promise<Tauri.Event.UnlistenFn> =
        current().onCloseRequested handler

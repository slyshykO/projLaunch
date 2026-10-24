module GitWatch

open System
open Fable.Core
open state

type private Observer =
    abstract dispose: unit -> unit

[<Import("watchRepository", "./interop/_GitWatch.js")>]
let private observe
    (listen: (GitProtocol.ChangeEvent -> unit) -> JS.Promise<Tauri.Event.UnlistenFn>)
    (subscribe: string -> JS.Promise<bool>)
    (unsubscribe: string -> JS.Promise<bool>)
    (matches: GitProtocol.ChangeEvent -> bool)
    (onChange: bool -> bool -> unit)
    (onStatus: string option -> unit)
    (newId: unit -> string)
    : Observer =
    jsNative

let subscriptions state : Elmish.Sub<Msg> =
    if not (AppTauri.Core.isAvailable ()) || state.isWindowClosePending then
        []
    else
        watchedRepositories state
        |> List.map (fun (endpoint, root) ->
            [ "git-watch"; GitProtocol.endpointKey endpoint; root ],
            (fun dispatch ->
                let observer =
                    observe
                        AppTauri.Git.onChanged
                        (AppTauri.Git.watch endpoint root)
                        (AppTauri.Git.unwatch endpoint)
                        (fun event -> GitProtocol.endpointKey event.endpoint = GitProtocol.endpointKey endpoint)
                        (fun history workingTree -> dispatch (GitChanged(endpoint, root, history, workingTree)))
                        (fun error -> dispatch (GitWatchStatus(endpoint, root, error)))
                        (fun () -> Guid.NewGuid().ToString())

                { new IDisposable with
                    member _.Dispose() = observer.dispose ()
                }))

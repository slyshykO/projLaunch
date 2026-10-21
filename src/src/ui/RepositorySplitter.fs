module RepositorySplitter

open Feliz
open Fable.Core

[<Import("attachSplitter", "../interop/_Splitter.js")>]
let private attach (element: Browser.Types.HTMLElement) (onResize: bool -> float -> unit) : (unit -> unit) = jsNative

[<ReactComponent>]
let Handle tabId (width: float) (height: float) dispatch =
    let element = React.useRef<Browser.Types.HTMLElement option> None

    React.useEffect (
        (fun () ->
            let dispose =
                element.current
                |> Option.map (fun element ->
                    attach element (fun horizontal value ->
                        dispatch (state.ResizeRepositoryPanels(tabId, horizontal, value))))
                |> Option.defaultValue ignore

            { new System.IDisposable with
                member _.Dispose() = dispose ()
            }),
        [| box tabId; box width; box height |]
    )

    Html.div
        [
            prop.ref element
            prop.role "separator"
            prop.tabIndex 0
            prop.ariaLabel "Resize file panel"
            prop.custom ("aria-controls", $"files-panel-{tabId}")
            prop.ariaValueMin 20
            prop.ariaValueMax 70
            prop.ariaValueNow width
            prop.className "repository-splitter"
            prop.title "Drag to resize panels, use arrow keys, or double-click to reset"
        ]

namespace Tauri

module rec Event =

    open Fable.Core

    [<AllowNullLiteral>]
    [<Interface>]
    type EventTarget =
        abstract member kind: string with get, set
        abstract member label: string with get, set

    [<AllowNullLiteral>]
    [<Interface>]
    type Event<'T> =
        /// <summary>
        /// Event name
        /// </summary>
        abstract member event: EventName with get, set
        /// <summary>
        /// Event identifier used to unlisten
        /// </summary>
        abstract member id: float with get, set
        /// <summary>
        /// Event payload
        /// </summary>
        abstract member payload: 'T with get, set

    [<AllowNullLiteral>]
    [<Interface>]
    type EventCallback<'T> =
        [<Emit("$0($1...)")>]
        abstract member Invoke: event: Event<'T> -> unit


    type UnlistenFn = unit -> unit


    [<RequireQualifiedAccess>]
    [<StringEnum(CaseRules.None)>]
    type TauriEvent =
        | [<CompiledName("tauri://resize")>] WINDOW_RESIZED
        | [<CompiledName("tauri://move")>] WINDOW_MOVED
        | [<CompiledName("tauri://close-requested")>] WINDOW_CLOSE_REQUESTED
        | [<CompiledName("tauri://destroyed")>] WINDOW_DESTROYED
        | [<CompiledName("tauri://focus")>] WINDOW_FOCUS
        | [<CompiledName("tauri://blur")>] WINDOW_BLUR
        | [<CompiledName("tauri://scale-change")>] WINDOW_SCALE_FACTOR_CHANGED
        | [<CompiledName("tauri://theme-changed")>] WINDOW_THEME_CHANGED
        | [<CompiledName("tauri://window-created")>] WINDOW_CREATED
        | [<CompiledName("tauri://webview-created")>] WEBVIEW_CREATED
        | [<CompiledName("tauri://drag-enter")>] DRAG_ENTER
        | [<CompiledName("tauri://drag-over")>] DRAG_OVER
        | [<CompiledName("tauri://drag-drop")>] DRAG_DROP
        | [<CompiledName("tauri://drag-leave")>] DRAG_LEAVE

    type EventName = U2<string, obj>

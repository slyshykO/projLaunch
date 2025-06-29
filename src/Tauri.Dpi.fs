namespace Tauri

module rec Dpi =

    open Fable.Core
    open Fable.Core.JsInterop
    open System

    [<Import("SERIALIZE_TO_IPC_FN", "@tauri-apps/api/core")>]
    let SERIALIZE_TO_IPC_FN: string = jsNative




    [<AllowNullLiteral>]
    [<Interface>]
    type LogicalSize =
        abstract member ``type``: string with get
        abstract member width: float with get, set
        abstract member height: float with get, set
        abstract member toPhysical: scaleFactor: float -> PhysicalSize
        //abstract member [SERIALIZE_TO_IPC_FN]: unit -> unit
        abstract member toJSON: unit -> unit

    [<AllowNullLiteral>]
    [<Interface>]
    type PhysicalSize =
        abstract member ``type``: string with get
        abstract member width: float with get, set
        abstract member height: float with get, set
        abstract member toLogical: scaleFactor: float -> LogicalSize
        //abstract member [SERIALIZE_TO_IPC_FN]: unit -> unit
        abstract member toJSON: unit -> unit

    [<AllowNullLiteral>]
    [<Interface>]
    type Size =
        abstract member size: U2<LogicalSize, PhysicalSize> with get, set
        abstract member toLogical: scaleFactor: float -> LogicalSize
        abstract member toPhysical: scaleFactor: float -> PhysicalSize
        //abstract member [SERIALIZE_TO_IPC_FN]: unit -> unit
        abstract member toJSON: unit -> unit

    [<AllowNullLiteral>]
    [<Interface>]
    type LogicalPosition =
        abstract member ``type``: string with get
        abstract member x: float with get, set
        abstract member y: float with get, set
        abstract member toPhysical: scaleFactor: float -> PhysicalPosition
        //abstract member [SERIALIZE_TO_IPC_FN]: unit -> unit
        abstract member toJSON: unit -> unit

    [<AllowNullLiteral>]
    [<Interface>]
    type PhysicalPosition =
        abstract member ``type``: string with get
        abstract member x: float with get, set
        abstract member y: float with get, set
        abstract member toLogical: scaleFactor: float -> LogicalPosition
        //abstract member [SERIALIZE_TO_IPC_FN]: unit -> unit
        abstract member toJSON: unit -> unit

    [<AllowNullLiteral>]
    [<Interface>]
    type Position =
        abstract member position: U2<LogicalPosition, PhysicalPosition> with get, set
        abstract member toLogical: scaleFactor: float -> LogicalPosition
        abstract member toPhysical: scaleFactor: float -> PhysicalPosition
        //abstract member [SERIALIZE_TO_IPC_FN]: unit -> unit
        abstract member toJSON: unit -> unit

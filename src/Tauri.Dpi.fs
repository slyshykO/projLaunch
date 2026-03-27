namespace Tauri

module rec Dpi =

    open Fable.Core

    [<Import("SERIALIZE_TO_IPC_FN", "@tauri-apps/api/core")>]
    let SERIALIZE_TO_IPC_FN: string = jsNative

    [<AllowNullLiteral>]
    [<Import("LogicalSize", "@tauri-apps/api/dpi")>]
    type LogicalSize(width: float, height: float) =
        member _.``type``: string = nativeOnly
        member _.width with get (): float = nativeOnly and set (_: float): unit = nativeOnly
        member _.height with get (): float = nativeOnly and set (_: float): unit = nativeOnly
        member _.toPhysical(_scaleFactor: float): PhysicalSize = nativeOnly
        member _.toJSON(): obj = nativeOnly

    [<AllowNullLiteral>]
    [<Import("PhysicalSize", "@tauri-apps/api/dpi")>]
    type PhysicalSize(width: float, height: float) =
        member _.``type``: string = nativeOnly
        member _.width with get (): float = nativeOnly and set (_: float): unit = nativeOnly
        member _.height with get (): float = nativeOnly and set (_: float): unit = nativeOnly
        member _.toLogical(_scaleFactor: float): LogicalSize = nativeOnly
        member _.toJSON(): obj = nativeOnly

    [<AllowNullLiteral>]
    [<Import("Size", "@tauri-apps/api/dpi")>]
    type Size(size: U2<LogicalSize, PhysicalSize>) =
        member _.size with get (): U2<LogicalSize, PhysicalSize> = nativeOnly and set (_: U2<LogicalSize, PhysicalSize>): unit = nativeOnly
        member _.toLogical(_scaleFactor: float): LogicalSize = nativeOnly
        member _.toPhysical(_scaleFactor: float): PhysicalSize = nativeOnly
        member _.toJSON(): obj = nativeOnly

    [<AllowNullLiteral>]
    [<Import("LogicalPosition", "@tauri-apps/api/dpi")>]
    type LogicalPosition(x: float, y: float) =
        member _.``type``: string = nativeOnly
        member _.x with get (): float = nativeOnly and set (_: float): unit = nativeOnly
        member _.y with get (): float = nativeOnly and set (_: float): unit = nativeOnly
        member _.toPhysical(_scaleFactor: float): PhysicalPosition = nativeOnly
        member _.toJSON(): obj = nativeOnly

    [<AllowNullLiteral>]
    [<Import("PhysicalPosition", "@tauri-apps/api/dpi")>]
    type PhysicalPosition(x: float, y: float) =
        member _.``type``: string = nativeOnly
        member _.x with get (): float = nativeOnly and set (_: float): unit = nativeOnly
        member _.y with get (): float = nativeOnly and set (_: float): unit = nativeOnly
        member _.toLogical(_scaleFactor: float): LogicalPosition = nativeOnly
        member _.toJSON(): obj = nativeOnly

    [<AllowNullLiteral>]
    [<Import("Position", "@tauri-apps/api/dpi")>]
    type Position(position: U2<LogicalPosition, PhysicalPosition>) =
        member _.position with get (): U2<LogicalPosition, PhysicalPosition> = nativeOnly and set (_: U2<LogicalPosition, PhysicalPosition>): unit = nativeOnly
        member _.toLogical(_scaleFactor: float): LogicalPosition = nativeOnly
        member _.toPhysical(_scaleFactor: float): PhysicalPosition = nativeOnly
        member _.toJSON(): obj = nativeOnly

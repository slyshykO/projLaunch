module FrontendLog

open System
open Fable.Core

[<Import("start", "./_Logging.js")>]
let private startCapture () : (unit -> unit) = jsNative

[<Import("write", "./_Logging.js")>]
let private write (level: string) (message: string) : unit = jsNative

[<Import("flush", "./_Logging.js")>]
let flush () : JS.Promise<unit> = jsNative

let debug message = write "debug" message
let info message = write "info" message
let error message = write "error" message

let start () =
    let stop = startCapture ()

    { new IDisposable with
        member _.Dispose() = stop ()
    }

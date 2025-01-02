module Tauri

open Fable.Core

[<Erase>]
type Tauri =

    [<Import("invoke", "@tauri-apps/api/tauri")>]
    static member invoke(_cmd: string, ?_invokeParams: obj) : JS.Promise<_> = jsNative

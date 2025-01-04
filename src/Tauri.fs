module Tauri

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom

[<Erase>]
type Tauri =

    [<Import("invoke", "@tauri-apps/api/core")>]
    static member invoke(_cmd: string, _invokeParams: obj option) : JS.Promise<_> = jsNative

    [<Import("isTauri", "@tauri-apps/api/core")>]
    static member isTauri() : bool = nativeOnly

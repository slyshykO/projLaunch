module ReactVersion

open Fable.Core

(*
import React from 'react';
let a = React.version
*)

[<Import("_reactVersion", "./_ReactVersion.js")>]
let _reactVersion: unit -> string = jsNative

let reactVersion () = _reactVersion ()

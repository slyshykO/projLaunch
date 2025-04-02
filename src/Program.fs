module Main

// import *css
//Fable.Core.JsInterop.importAll "./index.css"

open Elmish
open Elmish.React

open state
open view

Program.mkProgram init update view
|> Program.withReactSynchronous "feliz-app-364e6a85-5c8c-4f74-a6c4-470c3700aadb"
|> Program.run

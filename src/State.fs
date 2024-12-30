module state

open Elmish
open Feliz.Router

module Debug =

#if DEBUG
    let Debug = true
#else
    let Debug = false
#endif

type SignIn = { nik: string; role: string }

type Msg =
    | Empty
    | UrlChanged of string list

type State =
    { currentUrl: string list
      signIn: option<SignIn>
      value: string }

let getCurrentUrl () = Browser.Dom.window.location.href

let init () =

    let currentUrl = Router.currentUrl ()
    // Initial state
    { currentUrl = currentUrl
      signIn = None
      value = "Feliz" },
    Cmd.ofMsg Empty

let update msg state =
    match msg with
    | Empty -> state, Cmd.none
    | UrlChanged url -> { state with currentUrl = url }, Cmd.none

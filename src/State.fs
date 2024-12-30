module state

open Elmish
open Feliz.Router
open System
open Thoth.Json

module Debug =

#if DEBUG
    let Debug = true

    let projectsJsonStr =
        """
    [
        {
            "name": "Feliz",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Feliz is a happy web framework for F#",
            "path": "./Feliz",
            "ide": "VSCode",
            "environment": {"EXTRA": "1"}
        },
        {
            "name": "Fable",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Fable is a compiler that generates JavaScript from F# code",
            "path": "./Fable",
            "ide": "VSCode",
            "environment": {"EXTRA": "2"}
        },
        {
            "name": "Thoth",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Thoth is a JSON decoder and encoder for F#",
            "path": "./Thoth",
            "ide": "VSCode",
            "environment": {"EXTRA": "3"}
        },
        {
            "name": "Zafir",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Zafir is a web framework for F#",
            "path": "./Zafir",
            "ide": "VSCode",
            "environment": {"EXTRA": "4"}
        },
        {
            "name": "Daisy",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Daisy is a UI library for F#",
            "path": "./Daisy",
            "ide": "VSCode",
            "environment": {"EXTRA": "5"}
        },
        {
            "name": "Femto",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Femto is a web framework for F#",
            "path": "./Femto",
            "ide": "VSCode",
            "environment": {"EXTRA": "6"}
        },
        {
            "name": "Femto",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Femto is a web framework for F#",
            "path": "./Femto",
            "ide": "VSCode",
            "environment": {"EXTRA": "7"}
        }
    ]
    """
#else
    let Debug = false
    let projectsJsonStr = ""
#endif


let jsExtra =
    Extra.empty
    |> Extra.withInt64
    |> Extra.withUInt64
    |> Extra.withDecimal
    |> Extra.withBigInt

type ProjectData =
    { name: string
      lastOpened: DateTime
      description: string
      path: string
      ide: string
      environment: Map<string, string> }

    static member Default() =
        { name = ""
          lastOpened = (DateTime.Now)
          description = ""
          path = ""
          ide = ""
          environment = Map.empty }

    static member fromJson(json: string) =
        try
            match Decode.Auto.fromString<ProjectData> (json, extra = jsExtra) with
            | Ok pd -> pd
            | Error err -> failwith err
        with ex ->
            failwith ex.Message

    static member toJson(pd: ProjectData) =
        Encode.Auto.toString (pd, extra = jsExtra)

let listOfProjectDataFromJsonStr (jsonStr: string) =
    try
        match Decode.Auto.fromString<ProjectData list> (jsonStr, extra = jsExtra) with
        | Ok pd -> pd
        | Error err -> failwith err
    with ex ->
        failwith ex.Message

type SignIn = { nik: string; role: string }

type Msg =
    | Empty
    | UrlChanged of string list

type State =
    { currentUrl: string list
      signIn: option<SignIn>
      value: string
      projects: ProjectData list }

let getCurrentUrl () = Browser.Dom.window.location.href

let init () =

    let currentUrl = Router.currentUrl ()

    let pd =
        if Debug.Debug then
            listOfProjectDataFromJsonStr Debug.projectsJsonStr
        else
            []

    printfn "Projects: %A" pd

    // Initial state
    { currentUrl = currentUrl
      signIn = None
      value = "Feliz"
      projects = pd },
    Cmd.ofMsg Empty

let update msg state =
    match msg with
    | Empty -> state, Cmd.none
    | UrlChanged url -> { state with currentUrl = url }, Cmd.none

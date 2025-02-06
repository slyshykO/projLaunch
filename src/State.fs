module state

open Elmish
open Feliz.Router
open System
open Thoth.Json
open Fable.Core.JsInterop


open Tauri

module Debug =

#if DEBUG
    [<Literal>]
    let Debug = true

    let projectsJsonStr =
        """
    [
        {
            "id": "0",
            "name": "Feliz",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Feliz is a happy web framework for F#",
            "path": "./Feliz",
            "ide": "VSCode",
            "environment": {"EXTRA": "1"}
        },
        {
            "id": "1",
            "name": "Fable",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Fable is a compiler that generates JavaScript from F# code",
            "path": "./Fable",
            "ide": "VSCode",
            "environment": {"EXTRA": "2"}
        },
        {
            "id": "2",
            "name": "Thoth",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Thoth is a JSON decoder and encoder for F#",
            "path": "./Thoth",
            "ide": "VSCode",
            "environment": {"EXTRA": "3"}
        },
        {
            "id": "3",
            "name": "Zafir",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Zafir is a web framework for F#",
            "path": "./Zafir",
            "ide": "VSCode",
            "environment": {"EXTRA": "4"}
        },
        {
            "id": "4",
            "name": "Daisy",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Daisy is a UI library for F#",
            "path": "./Daisy",
            "ide": "VSCode",
            "environment": {"EXTRA": "5"}
        },
        {
            "id": "5",
            "name": "Femto",
            "lastOpened": "2021-09-01T00:00:00",
            "description": "Femto is a web framework for F#",
            "path": "./Femto",
            "ide": "VSCode",
            "environment": {"EXTRA": "6"}
        },
        {
            "id": "6",
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
    [<Literal>]
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
    { id: string
      name: string
      lastOpened: DateTime
      description: string
      path: string
      ide: string
      environment: Map<string, string> }

    static member Default() =
        { id = ""
          name = ""
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

type SignIn = { nick: string; role: string }

type Msg =
    | Empty
    | Start
    | OnStart of unit
    | UrlChanged of string list
    | Greet of string
    | OnGreetSuccess of string
    | GetDataDir
    | OnGetDataDir of string
    | OnGetDataDirError of exn
    | GetConfigDir
    | OnGetConfigDir of string
    | OnGetConfigDirError of exn
    | GetAppVersion
    | OnAppVersion of string
    | OnAppVersionError of exn

    | GetProjects of int
    | OnGetProjectsSuccess of string array
    | OnGetProjectsError of exn

    | OpenProject of string
    | OnOpenProjectSuccess of string
    | OnOpenProjectError of exn

    | AddOrUpdateProject of string * string
    | OnAddOrUpdateProjectSuccess of string
    | OnAddOrUpdateProjectError of exn



type State =
    { currentUrl: string list
      signIn: option<SignIn>
      value: string
      appVersion: string
      appDataDir: string // C:\Users\alex\AppData\Roaming\com.projlaunch.app
      appConfigDir: string // C:\Users\alex\AppData\Roaming\com.projlaunch.app
      errors: string list
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
    let state =
        { currentUrl = currentUrl
          signIn = None
          value = "Feliz"
          appVersion = ""
          appDataDir = ""
          appConfigDir = ""
          errors = []
          projects = pd }

    state, Cmd.ofMsg Start

let update msg state =
    match msg with
    | Empty -> state, Cmd.none
    | Start ->
        let delayPromise () =
            promise {
                do! Promise.sleep 100
                return ()
            }

        state, Cmd.OfPromise.perform delayPromise () OnStart

    | OnStart() ->
        if Tauri.isTauri () then
            state,
            Cmd.batch
                [ Cmd.ofMsg (GetProjects 50)
                  Cmd.ofMsg GetConfigDir
                  Cmd.ofMsg GetDataDir
                  Cmd.ofMsg GetAppVersion ]
        else
            state, Cmd.none

    | UrlChanged url -> { state with currentUrl = url }, Cmd.none

    | Greet nick ->
        let greetPromise n =
            promise {
                let! (response: string) = (Tauri.invoke ("greet", (Some(createObj [ "name" ==> n ]))))
                return response
            }

        state, Cmd.OfPromise.perform greetPromise nick OnGreetSuccess
    | OnGreetSuccess response ->
        let newState = { state with value = response }
        newState, Cmd.none

    | GetDataDir ->
        let getDataDirPromise () =
            promise {
                do! Promise.sleep 50
                let! (response: string) = Tauri.appDataDir ()
                return response
            }

        state, Cmd.OfPromise.either getDataDirPromise () OnGetDataDir OnGetDataDirError

    | OnGetDataDir response ->
        let newState = { state with appDataDir = response }
        newState, Cmd.none

    | OnGetDataDirError exn ->
        let e = sprintf "Error: %A" exn
        let newState = { state with appDataDir = e }
        newState, Cmd.none

    | GetConfigDir ->
        let getConfigDirPromise () =
            promise {
                do! Promise.sleep 50
                let! (response: string) = Tauri.appConfigDir ()
                return response
            }

        state, Cmd.OfPromise.either getConfigDirPromise () OnGetConfigDir OnGetConfigDirError
    | OnGetConfigDir response ->
        let newState = { state with appConfigDir = response }
        newState, Cmd.none
    | OnGetConfigDirError exn ->
        let e = sprintf "Error: %A" exn
        let newState = { state with appConfigDir = e }
        newState, Cmd.none

    | GetAppVersion ->
        let getAppVersionPromise () =
            promise {
                do! Promise.sleep 50
                let! (response: string) = Tauri.getVersion ()
                return response
            }

        state, Cmd.OfPromise.either getAppVersionPromise () OnAppVersion OnAppVersionError
    | OnAppVersion response ->
        let newState = { state with appVersion = response }
        newState, Cmd.none
    | OnAppVersionError exn ->
        let e = sprintf "Error: %A" exn
        let newState = { state with appVersion = e }
        newState, Cmd.none

    | GetProjects timeout ->
        let getProjectsPromise () =
            promise {
                if timeout > 0 then
                    do! Promise.sleep timeout

                let! (response: string array) = Tauri.invoke ("get_projects", None)
                return response
            }

        state, Cmd.OfPromise.either getProjectsPromise () OnGetProjectsSuccess OnGetProjectsError
    | OnGetProjectsSuccess response ->
        let mutable err = []
        let mutable prj: ProjectData list = []

        for r in response do
            try
                let pd = ProjectData.fromJson r
                prj <- List.append prj [ pd ]
            with ex ->
                let s = sprintf "%A" ex
                err <- List.append err [ s ]
                ()

        // sort by last opened
        prj <- prj |> List.sortByDescending (fun p -> p.lastOpened)

        let newState =
            { state with
                errors = err |> List.append state.errors
                projects = prj }

        newState, Cmd.none
    | OnGetProjectsError exn ->
        let e = sprintf "Error: %A" exn
        let newErrors = List.append state.errors [ e ]
        let newState = { state with errors = newErrors }
        newState, Cmd.none

    | OpenProject id ->
        let openProjectPromise p =
            promise {
                do! (Tauri.invoke ("open_project", (Some(createObj [ "id" ==> p ]))))
                return id
            }

        state, Cmd.OfPromise.either openProjectPromise id OnOpenProjectSuccess OnOpenProjectError

    | OnOpenProjectSuccess id ->
        let projects = state.projects
        // get the project by id
        let prj = projects |> List.find (fun p -> p.id = id)
        let prj = { prj with lastOpened = DateTime.Now }
        let newProjects = projects |> List.map (fun p -> if p.id = id then prj else p)

        let prjJson = ProjectData.toJson prj

        let newState = { state with projects = newProjects }
        newState, (Cmd.ofMsg (AddOrUpdateProject(id, prjJson)))

    | OnOpenProjectError exn ->
        let e = sprintf "Error: %A" exn
        let newErrors = List.append state.errors [ e ]
        let newState = { state with errors = newErrors }
        newState, Cmd.none

    | AddOrUpdateProject(id, json) ->
        let addOrUpdateProjectPromise (i, j) =
            promise {
                do! Tauri.invoke ("rewrite_project_file", (Some(createObj [ "id" ==> i; "content" ==> j ])))
                return id
            }

        state,
        Cmd.OfPromise.either addOrUpdateProjectPromise (id, json) OnAddOrUpdateProjectSuccess OnAddOrUpdateProjectError
    | OnAddOrUpdateProjectSuccess id -> state, Cmd.ofMsg (GetProjects 1000)
    | OnAddOrUpdateProjectError exn ->
        let e = sprintf "Error: %A" exn
        let newErrors = List.append state.errors [ e ]
        let newState = { state with errors = newErrors }
        newState, Cmd.none

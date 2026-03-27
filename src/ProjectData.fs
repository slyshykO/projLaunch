module projectData

open System
open Thoth.Json
open Serde.FS
open Serde.FS.Json

let jsExtra =
    Extra.empty
    |> Extra.withInt64
    |> Extra.withUInt64
    |> Extra.withDecimal
    |> Extra.withBigInt

(*
#[derive(Debug, Clone, Deserialize, Serialize)]
enum Remote {
    Ssh { host: String, username: String },
    Wsl(String),
}
*)

[<Serde>]
type Remote =
    | Ssh of host: string * username: string
    | Wsl of string

[<Serde>]
type ProjectData =
    { id: string
      name: string
      lastOpened: DateTime
      description: string
      path: string
      ide: string
      environment: Map<string, string>
      remote: Remote option }

    static member Default() =
        { id = ""
          name = ""
          lastOpened = DateTime.Now
          description = ""
          path = ""
          ide = ""
          environment = Map.empty
          remote = None }

    static member fromJson(json: string) =
        try
            match Decode.Auto.fromString<ProjectData> (json, extra = jsExtra) with
            | Ok pd -> pd
            | Error err -> failwith err
        with ex ->
            printfn "Error deserializing JSON: %s" ex.Message
            failwith ex.Message

    static member toJson(pd: ProjectData) =
        Encode.Auto.toString (pd, extra = jsExtra)

let listOfProjectDataFromJsonStr (jsonStr: string) =
    try
        match Decode.Auto.fromString<ProjectData list> (jsonStr, extra = jsExtra) with
        | Ok pd -> pd
        | Error err -> failwith err
    with ex ->
        printfn "Error deserializing JSON: %s" ex.Message
        failwith ex.Message

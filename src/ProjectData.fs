module projectData

open System
open Thoth.Json
open Serde.FS

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

let private remoteEncoder (remote: Remote) =
    match remote with
    | Ssh (host, username) ->
        Encode.object
            [ "Ssh",
              Encode.object
                  [ "host", Encode.string host
                    "username", Encode.string username ] ]
    | Wsl distro ->
        Encode.object [ "Wsl", Encode.string distro ]

let private serdeRemoteDecoder : Decoder<Remote> =
    Decode.oneOf
        [ Decode.field
              "Ssh"
              (Decode.object (fun get -> Ssh(get.Required.Field "host" Decode.string, get.Required.Field "username" Decode.string)))
          Decode.field "Wsl" (Decode.map Wsl Decode.string) ]

let private thothRemoteDecoder : Decoder<Remote> =
    Decode.index 0 Decode.string
    |> Decode.andThen (fun caseName ->
        match caseName with
        | "Ssh" ->
            Decode.map2
                (fun host username -> Ssh(host, username))
                (Decode.index 1 Decode.string)
                (Decode.index 2 Decode.string)
        | "Wsl" -> Decode.map Wsl (Decode.index 1 Decode.string)
        | _ -> Decode.fail $"Unknown remote case: {caseName}")

let private remoteDecoder : Decoder<Remote> =
    Decode.oneOf [ serdeRemoteDecoder; thothRemoteDecoder ]

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

let private environmentEncoder (environment: Map<string, string>) =
    environment
    |> Map.map (fun _ value -> Encode.string value)
    |> Encode.dict

let private projectDataEncoder (pd: ProjectData) =
    Encode.object
        [ "id", Encode.string pd.id
          "name", Encode.string pd.name
          "lastOpened", Encode.datetime pd.lastOpened
          "description", Encode.string pd.description
          "path", Encode.string pd.path
          "ide", Encode.string pd.ide
          "environment", environmentEncoder pd.environment
          "remote", Encode.option remoteEncoder pd.remote ]

let private projectDataDecoder : Decoder<ProjectData> =
    Decode.object (fun get ->
        { id = get.Required.Field "id" Decode.string
          name = get.Required.Field "name" Decode.string
          lastOpened = get.Required.Field "lastOpened" Decode.datetimeUtc
          description = get.Required.Field "description" Decode.string
          path = get.Required.Field "path" Decode.string
          ide = get.Required.Field "ide" Decode.string
          environment = get.Required.Field "environment" (Decode.dict Decode.string)
          remote = get.Optional.Field "remote" remoteDecoder })

type ProjectData with
    static member fromJson(json: string) =
        try
            match Decode.fromString projectDataDecoder json with
            | Ok pd -> pd
            | Error err -> failwith err
        with ex ->
            printfn "Error deserializing JSON: %s" ex.Message
            failwith ex.Message

    static member toJson(pd: ProjectData) =
        projectDataEncoder pd
        |> Encode.toString 0

let listOfProjectDataFromJsonStr (jsonStr: string) =
    try
        match Decode.fromString (Decode.list projectDataDecoder) jsonStr with
        | Ok pd -> pd
        | Error err -> failwith err
    with ex ->
        printfn "Error deserializing JSON: %s" ex.Message
        failwith ex.Message

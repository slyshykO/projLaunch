module projectData

open System
open Thoth.Json

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
          lastOpened = DateTime.Now
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

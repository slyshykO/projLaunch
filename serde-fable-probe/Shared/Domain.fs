namespace SerdeFableProbe

open System
open Serde.FS

type ProbeRecord = { Name: string; Count: int }

// Mirrors the data shapes in src/src/domain/ProjectData.fs without its Thoth implementation.
type Remote =
    | Ssh of host: string * username: string
    | Wsl of string

type ProjectData =
    {
        id: string
        name: string
        lastOpened: DateTime
        description: string
        path: string
        ide: string
        environment: Map<string, string>
        remote: Remote option
    }

[<RpcApi>]
type IProbeApi =
    abstract Echo: ProbeRecord -> Async<ProbeRecord>
    abstract EchoProject: ProjectData -> Async<ProjectData>

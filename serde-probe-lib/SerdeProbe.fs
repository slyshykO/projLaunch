namespace SerdeProbeLib

open Serde.FS
open Serde.FS.Json

[<Serde>]
type ProbeRecord =
    { Name: string
      Count: int }

module Probe =
    let sample =
        { Name = "serde-probe"
          Count = 7 }

    let toJson (value: ProbeRecord) =
        SerdeJson.serialize value

    let fromJson (json: string) =
        SerdeJson.deserialize<ProbeRecord> json

    let sampleJson =
        toJson sample

module SerdeFableProbe.Client

open System
open SerdeFableProbe
open SerdeGenerated.Fable

let echo () =
    async {
        let api = IProbeApiFableClient.create "http://serde-probe.invalid"
        let! result = api.Echo { Name = "serde-probe"; Count = 7 }

        if result.Name <> "serde-probe" || result.Count <> 7 then
            failwith "Record round-trip mismatch"

        return result.Name
    }

let echoProject (remoteCase: int) =
    async {
        let sample =
            {
                id = "probe"
                name = "Serde probe"
                lastOpened = DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc)
                description = "Fable compatibility"
                path = "E:/Projects/probe"
                ide = "VSCode"
                environment = Map.ofList [ "EXTRA", "1"; "UNICODE", "тест" ]
                remote =
                    match remoteCase with
                    | 0 -> None
                    | 1 -> Some(Wsl "Ubuntu")
                    | _ -> Some(Ssh("example.test", "user"))
            }

        let api = IProbeApiFableClient.create "http://serde-probe.invalid"
        let! result = api.EchoProject sample

        if result.environment <> sample.environment then
            failwithf "Environment mismatch: decoded count=%d" result.environment.Count
        // The generated DateTime.Parse produces local time; compare UTC instants.
        let normalized =
            { result with
                lastOpened = result.lastOpened.ToUniversalTime()
            }

        if normalized <> sample then
            failwithf "Project round-trip mismatch: %A" result

        return result.id
    }

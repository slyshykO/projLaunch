module GitServerUbuntu22

open System
open System.IO

let build (run: bool -> string -> string list -> string) projectRoot distribution isRelease =
    let script =
        Path.Combine(projectRoot, "build", "scripts", "build", "git-server-ubuntu22.sh")

    let arguments = if isRelease then [ "--release" ] else []

    if OperatingSystem.IsWindows() then
        let wslArguments =
            distribution
            |> Option.map (fun name -> [ "--distribution"; name ])
            |> Option.defaultValue []

        let linuxScript =
            run true "wsl.exe" (wslArguments @ [ "--exec"; "wslpath"; "-a"; "-u"; script ])
            |> fun path -> path.Trim()

        if String.IsNullOrWhiteSpace linuxScript then
            failwith $"WSL could not translate the build script path: {script}"

        run false "wsl.exe" (wslArguments @ [ "--exec"; "bash"; linuxScript ] @ arguments)
        |> ignore
    elif OperatingSystem.IsLinux() then
        if distribution.IsSome then
            failwith "--wsl-distro is only supported when building on Windows"

        run false "bash" (script :: arguments) |> ignore
    else
        failwith "The ubuntu22 build requires Linux or Windows with WSL"

module GitServer

open System
open System.Diagnostics
open System.IO

let private projectRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

type Target =
    | Native
    | Ubuntu22 of distribution: string option

let parseOptions (arguments: string list) =
    let rec parse target isRelease distribution remaining =
        match remaining with
        | [] ->
            match target, distribution with
            | Some "ubuntu22", _ -> isRelease, Ubuntu22 distribution
            | _, Some _ -> failwith "--wsl-distro requires the ubuntu22 target"
            | _ -> isRelease, Native
        | "--release" :: tail when not isRelease -> parse target true distribution tail
        | "--wsl-distro" :: name :: tail when
            distribution.IsNone
            && not (String.IsNullOrWhiteSpace name)
            && not (name.StartsWith "--")
            ->
            parse target isRelease (Some name) tail
        | ("native" | "ubuntu22" as name) :: tail when target.IsNone -> parse (Some name) isRelease distribution tail
        | _ -> failwith "Usage: build build-git-server [native|ubuntu22] [--release] [--wsl-distro NAME]"

    parse None false None arguments

let private run captureOutput command (arguments: string list) =
    Print.printlnBlue (String.concat " " (command :: arguments))
    let info = ProcessStartInfo(command, arguments)
    info.WorkingDirectory <- projectRoot
    info.UseShellExecute <- false
    info.CreateNoWindow <- true
    info.RedirectStandardOutput <- captureOutput
    use proc = new Process(StartInfo = info)
    proc.Start() |> ignore

    let output =
        if captureOutput then
            proc.StandardOutput.ReadToEnd()
        else
            ""

    proc.WaitForExit()

    if proc.ExitCode <> 0 then
        failwith $"{command} exited with code {proc.ExitCode}"

    output

let private targetTriple () =
    match
        Environment.GetEnvironmentVariable "TAURI_ENV_TARGET_TRIPLE"
        |> Option.ofObj
        |> Option.filter (String.IsNullOrWhiteSpace >> not)
    with
    | Some target -> target.Trim()
    | None ->
        let version = run true "rustc" [ "-vV" ]

        version.Split('\n')
        |> Array.tryPick (fun line ->
            if line.StartsWith("host: ", StringComparison.Ordinal) then
                Some(line.Substring(6).Trim())
            else
                None)
        |> Option.filter (String.IsNullOrWhiteSpace >> not)
        |> Option.defaultWith (fun () -> failwith "Cannot determine Rust target from rustc -vV")

let private buildNative isRelease =
    let target = targetTriple ()
    let targetDirectory = Path.Combine(projectRoot, "crates", "target")

    let arguments =
        [
            "build"
            "--manifest-path"
            Path.Combine(projectRoot, "crates", "Cargo.toml")
            "-p"
            "git-server"
            "--locked"
            "--target"
            target
            "--target-dir"
            targetDirectory
        ]
        @ if isRelease then [ "--release" ] else []

    run false "cargo" arguments |> ignore

    let extension = if target.Contains "windows" then ".exe" else ""
    let profile = if isRelease then "release" else "debug"

    let source =
        Path.Combine(targetDirectory, target, profile, $"project-launch-git-server{extension}")

    let binaryDirectory = Path.Combine(projectRoot, "src-tauri", "binaries")
    Directory.CreateDirectory binaryDirectory |> ignore

    let destination =
        Path.Combine(binaryDirectory, $"project-launch-git-server-{target}{extension}")

    File.Copy(source, destination, true)
    Print.printlnGreen $"Git server copied to {destination}"

let build isRelease target =
    try
        Versioning.sync projectRoot |> ignore

        match target with
        | Native -> buildNative isRelease
        | Ubuntu22 distribution -> GitServerUbuntu22.build run projectRoot distribution isRelease

        0
    with ex ->
        Print.printlnRed $"Error while building Git server: {ex.Message}"
        1

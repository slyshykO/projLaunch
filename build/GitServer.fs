module GitServer

open System
open System.Diagnostics
open System.IO

let private projectRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

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

let build isRelease =
    try
        Versioning.sync projectRoot |> ignore
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
            Path.Combine(targetDirectory, target, profile, $"git-server{extension}")

        let binaryDirectory = Path.Combine(projectRoot, "src-tauri", "binaries")
        Directory.CreateDirectory binaryDirectory |> ignore
        let destination = Path.Combine(binaryDirectory, $"git-server-{target}{extension}")
        File.Copy(source, destination, true)
        Print.printlnGreen $"Git server copied to {destination}"
        0
    with ex ->
        Print.printlnRed $"Error while building Git server: {ex.Message}"
        1

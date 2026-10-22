module Publish

open System
open System.IO
open System.IO.Compression
open System.Runtime.InteropServices
open System.Text.Json
open System.Text.RegularExpressions

let windowsServer = "project-launch-git-server-x86_64-pc-windows-msvc.exe"
let linuxServer = "project-launch-git-server-x86_64-unknown-linux-gnu"

let parseOptions =
    function
    | [] -> None
    | [ "--wsl-distro"; name ] when not (String.IsNullOrWhiteSpace name) && not (name.StartsWith "--") -> Some name
    | _ -> failwith "Usage: build publish [--wsl-distro NAME]"

/// Package an explicit allowlist so stale binaries, symbols and user files cannot leak into a release.
let createArchive projectRoot =
    use manifest =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(projectRoot, "package.json")))

    let version =
        manifest.RootElement.GetProperty("version").GetString()
        |> Option.ofObj
        |> Option.defaultWith (fun () -> failwith "Package version is missing")

    if not (Regex.IsMatch(version, @"^[0-9][0-9A-Za-z.+-]*$")) then
        failwith "Invalid package version for the release archive name"

    let files =
        [
            Path.Combine(projectRoot, "src-tauri", "target", "release", "projlaunch.exe"), "projLaunch.exe"
            Path.Combine(projectRoot, "src-tauri", "binaries", windowsServer), $"binaries/{windowsServer}"
            Path.Combine(projectRoot, "src-tauri", "binaries", linuxServer), $"binaries/{linuxServer}"
        ]

    for source, _ in files do
        if not (File.Exists source) || FileInfo(source).Length = 0L then
            failwith $"Release artifact is missing or empty: {source}"

    let directory = Path.Combine(projectRoot, "build", "dist")
    Directory.CreateDirectory directory |> ignore
    let archivePath = Path.Combine(directory, $"projLaunch-{version}-windows-x64.zip")
    let temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.zip")

    try
        do
            use archive = ZipFile.Open(temporary, ZipArchiveMode.Create)

            for source, entryName in files do
                archive.CreateEntryFromFile(source, entryName, CompressionLevel.Optimal)
                |> ignore

        // Replace only after every entry and the ZIP directory have been written successfully.
        File.Move(temporary, archivePath, true)
        archivePath
    finally
        if File.Exists temporary then
            File.Delete temporary

let run buildApplication distribution =
    try
        if
            not (OperatingSystem.IsWindows())
            || RuntimeInformation.OSArchitecture <> Architecture.X64
        then
            failwith "Publishing currently requires Windows x64 with WSL"

        // Publishing uses the standard Tauri output path; do not accidentally package an old build.
        for variable in [ "CARGO_TARGET_DIR"; "CARGO_BUILD_TARGET" ] do
            if not (String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable variable)) then
                failwith $"Unset {variable} before publishing; the archive uses the standard Cargo output paths"

        let target = Environment.GetEnvironmentVariable "TAURI_ENV_TARGET_TRIPLE"

        if not (String.IsNullOrWhiteSpace target) && target <> "x86_64-pc-windows-msvc" then
            failwith "Publishing requires the x86_64-pc-windows-msvc Tauri target"

        // Fail early if WSL/toolchains are unavailable. The app build also builds the native sidecar.
        if GitServer.build true (GitServer.Ubuntu22 distribution) <> 0 then
            1
        elif buildApplication () <> 0 then
            1
        else
            let archive = createArchive Versioning.projectRoot
            Print.printlnGreen $"Release archive: {archive}"
            0
    with ex ->
        Print.printlnRed $"Publish failed: {ex.Message}"
        1

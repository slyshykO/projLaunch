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

/// Linux x64 portable archive: the GUI plus the native Git server only (no WSL, no Windows server).
let createLinuxArchive projectRoot =
    // Regex instead of JsonDocument: the trimmed Linux helper cannot load System.IO.Pipelines.
    let version =
        let m =
            Regex.Match(
                File.ReadAllText(Path.Combine(projectRoot, "package.json")),
                "\"version\"\\s*:\\s*\"([^\"]+)\""
            )

        if m.Success then
            m.Groups[1].Value
        else
            failwith "Package version is missing"

    if not (Regex.IsMatch(version, @"^[0-9][0-9A-Za-z.+-]*$")) then
        failwith "Invalid package version for the release archive name"

    let files =
        [
            Path.Combine(projectRoot, "src-tauri", "target", "release", "projlaunch"), "projLaunch"
            Path.Combine(projectRoot, "src-tauri", "binaries", linuxServer), $"binaries/{linuxServer}"
        ]

    for source, _ in files do
        if not (File.Exists source) || FileInfo(source).Length = 0L then
            failwith $"Release artifact is missing or empty: {source}"

    let directory = Path.Combine(projectRoot, "build", "dist")
    Directory.CreateDirectory directory |> ignore
    let archivePath = Path.Combine(directory, $"projLaunch-{version}-linux-x64.tar.gz")
    let staging = Path.Combine(directory, $".{Guid.NewGuid():N}")
    let temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tar.gz")

    try
        for source, entryName in files do
            let destination = Path.Combine(staging, entryName)
            Directory.CreateDirectory(Path.GetDirectoryName destination |> Option.ofObj |> Option.get)
            |> ignore

            File.Copy(source, destination)

        // tar keeps the executable bits that ZipArchive would drop.
        let startInfo = Diagnostics.ProcessStartInfo("tar", [ "-czf"; temporary; "-C"; staging; "." ])
        startInfo.UseShellExecute <- false
        use proc = Diagnostics.Process.Start startInfo

        proc.WaitForExit()

        if proc.ExitCode <> 0 then
            failwith $"tar failed with exit code {proc.ExitCode}"

        File.Move(temporary, archivePath, true)
        archivePath
    finally
        if File.Exists temporary then
            File.Delete temporary

        if Directory.Exists staging then
            Directory.Delete(staging, true)

let private checkCargoOverrides () =
    for variable in [ "CARGO_TARGET_DIR"; "CARGO_BUILD_TARGET" ] do
        if not (String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable variable)) then
            failwith $"Unset {variable} before publishing; the archive uses the standard Cargo output paths"

let private runLinux buildApplication =
    if RuntimeInformation.OSArchitecture <> Architecture.X64 then
        failwith "Linux publishing currently requires x64"

    checkCargoOverrides ()

    // The app build (npm prebuild) builds the native Git server sidecar.
    if buildApplication () <> 0 then
        1
    else
        let archive = createLinuxArchive Versioning.projectRoot
        Print.printlnGreen $"Release archive: {archive}"
        0

let run buildApplication distribution =
    try
        if OperatingSystem.IsLinux() then
            if Option.isSome distribution then
                failwith "--wsl-distro is only used on Windows"

            runLinux buildApplication
        else

        if
            not (OperatingSystem.IsWindows())
            || RuntimeInformation.OSArchitecture <> Architecture.X64
        then
            failwith "Publishing requires Windows x64 (with WSL) or Linux x64"

        // Publishing uses the standard Tauri output path; do not accidentally package an old build.
        checkCargoOverrides ()

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

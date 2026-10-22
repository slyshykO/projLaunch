// Build build.fsproj first, then pass its DLL with dotnet fsi --reference:...
open System
open System.IO
open System.IO.Compression

let check condition message =
    if not condition then
        failwith message

let testRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "obj", "publish-tests"))

let fixture = Path.Combine(testRoot, Guid.NewGuid().ToString("N"))

let write relative (content: string) =
    let path = Path.Combine(fixture, relative)
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllText(path, content)

try
    write "package.json" """{"version":"1.2.3+4.gabc"}"""
    write "src-tauri/target/release/projlaunch.exe" "release GUI"
    write "src-tauri/target/debug/projlaunch.exe" "wrong debug GUI"
    write $"src-tauri/binaries/{Publish.windowsServer}" "Windows server"
    write $"src-tauri/binaries/{Publish.linuxServer}" "Linux server"
    write "src-tauri/binaries/old-server.exe" "must not be packaged"
    write "src-tauri/target/release/projlaunch.pdb" "must not be packaged"

    let archivePath = Publish.createArchive fixture

    check
        (Path.GetFileName archivePath = "projLaunch-1.2.3+4.gabc-windows-x64.zip")
        "Archive must include the manifest version"

    do
        use archive = ZipFile.OpenRead archivePath
        let names = archive.Entries |> Seq.map (fun entry -> entry.FullName) |> Set.ofSeq

        check
            (names =
                Set.ofList
                    [
                        "projLaunch.exe"
                        $"binaries/{Publish.windowsServer}"
                        $"binaries/{Publish.linuxServer}"
                    ])
            "Archive must have the exact portable layout, without a wrapper folder or stale artifacts"

    let extracted = Path.Combine(fixture, "Extracted With Spaces")
    ZipFile.ExtractToDirectory(archivePath, extracted)
    check (File.ReadAllText(Path.Combine(extracted, "projLaunch.exe")) = "release GUI") "Must package the release GUI"

    check
        (File.ReadAllText(Path.Combine(extracted, "binaries", Publish.linuxServer)) = "Linux server")
        "Linux artifact must survive extraction"

    write "src-tauri/target/release/projlaunch.exe" "updated release"
    check (Publish.createArchive fixture = archivePath) "Publishing the same version must replace the archive"
    let previous = File.ReadAllBytes archivePath
    File.Delete(Path.Combine(fixture, "src-tauri", "binaries", Publish.linuxServer))

    let rejected =
        try
            Publish.createArchive fixture |> ignore
            false
        with _ ->
            true

    check rejected "Missing required artifact must fail publishing"
    check (File.ReadAllBytes archivePath = previous) "Failed publishing must preserve the previous complete ZIP"
    check (Directory.GetFiles(Path.GetDirectoryName archivePath).Length = 1) "No temporary ZIP may remain"
    check (Publish.parseOptions [] = None) "Default distro"
    check (Publish.parseOptions [ "--wsl-distro"; "Ubuntu Test" ] = Some "Ubuntu Test") "Explicit distro"

    check
        (Program.Args.subCommand [| "publish" |] = Some Program.Args.SubCommand.Publish)
        "Publish command must be routed"

    printfn "PASS: release ZIP layout, content, replacement and failure preservation"
finally
    if
        Directory.Exists fixture
        && Path
            .GetFullPath(fixture)
            .StartsWith(testRoot + string Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
    then
        Directory.Delete(fixture, true)

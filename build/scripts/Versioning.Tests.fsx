#load "../Versioning.fs"

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Text.RegularExpressions

let testDirectory =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "obj", "version-tests"))

let fixture = Path.Combine(testDirectory, Guid.NewGuid().ToString("N"))
Directory.CreateDirectory fixture |> ignore

let git root (args: string list) =
    let info = ProcessStartInfo("git", args)
    info.WorkingDirectory <- root
    info.UseShellExecute <- false
    info.RedirectStandardOutput <- true
    info.RedirectStandardError <- true
    use proc = new Process(StartInfo = info)
    proc.Start() |> ignore
    let output = proc.StandardOutput.ReadToEndAsync()
    let error = proc.StandardError.ReadToEndAsync()
    proc.WaitForExit()

    if proc.ExitCode <> 0 then
        failwith (error.Result)

    output.Result.TrimEnd('\r', '\n')

let check condition message =
    if not condition then
        failwith message

let write path (content: string) =
    let destination = Path.Combine(fixture, path)
    Directory.CreateDirectory(Path.GetDirectoryName destination) |> ignore
    File.WriteAllText(destination, content)

let read path =
    File.ReadAllText(Path.Combine(fixture, path))

let run args = git fixture args

let commit () =
    run [ "add"; "." ] |> ignore
    run [ "commit"; "-m"; "Fixture" ] |> ignore

let expectFailure action message =
    let failed =
        try
            action ()
            false
        with _ ->
            true

    check failed message

let snapshot () =
    let files =
        (run [ "ls-files"; "-z" ]).Split('\000', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun path ->
            let fullPath = Path.Combine(fixture, path)
            path, File.ReadAllBytes fullPath, File.GetLastWriteTimeUtc fullPath)

    run [ "rev-parse"; "HEAD" ],
    run [ "show-ref"; "--tags" ],
    run [ "status"; "--porcelain" ],
    File.ReadAllBytes(Path.Combine(fixture, ".git", "index")),
    files

let expectDowngradeRejected version latest =
    let before = snapshot ()

    let error =
        try
            Versioning.setVersion fixture version
            ""
        with ex ->
            ex.Message

    check (error.Contains $"must be greater than the latest release {latest}") $"Wrong rejection for {version}: {error}"
    check (snapshot () = before) "Rejected version changed files, index, timestamps, HEAD or tags"

let rootVersion path =
    use json = JsonDocument.Parse(read path)
    json.RootElement.GetProperty("version").GetString()

let checkVersions version =
    for path in
        [
            "package.json"
            "src-tauri/tauri.conf.json"
            "package-lock.json"
            "nested/package.json"
        ] do
        check (rootVersion path = version) $"Wrong version in {path}"

    use lockJson = JsonDocument.Parse(read "package-lock.json")

    check
        (lockJson.RootElement.GetProperty("packages").GetProperty("").GetProperty("version").GetString() = version)
        "Wrong package-lock root package version"

    for path in [ "src-tauri/Cargo.toml"; "crates/Cargo.toml" ] do
        check ((read path).Contains($"version = \"{version}\"")) $"Wrong version in {path}"

    for path in [ "crates/Cargo.lock"; "src-tauri/Cargo.lock" ] do
        for block in (read path).Split("[[package]]") do
            if Regex.IsMatch(block, "(?m)^name = \"(?:projlaunch|git-client|git-server|git-protocol)\"$") then
                check (block.Contains($"version = \"{version}\"")) $"Wrong local package version in {path}"

    check ((read "crates/git-client/Cargo.toml").Contains("version.workspace = true")) "Workspace inheritance changed"

try
    // Exercise the actual repository manifests and lockfiles without touching its refs.
    for path in (git Versioning.projectRoot [ "ls-files"; "-z" ]).Split('\000', StringSplitOptions.RemoveEmptyEntries) do
        if
            [
                "Cargo.toml"
                "Cargo.lock"
                "package.json"
                "package-lock.json"
                "tauri.conf.json"
            ]
            |> List.contains (Path.GetFileName path)
        then
            write path (File.ReadAllText(Path.Combine(Versioning.projectRoot, path)))

    let nested =
        """{"name":"тест","version":"0.0.1","dependencies":{"version":"9.8.7"},"nested":{"version":"6.5.4"}}"""

    write "nested/package.json" nested
    write "notes.txt" "original"
    run [ "init" ] |> ignore
    run [ "config"; "user.name"; "Versioning Test" ] |> ignore
    run [ "config"; "user.email"; "versioning-test@example.invalid" ] |> ignore
    run [ "config"; "commit.gpgsign"; "false" ] |> ignore
    run [ "config"; "core.autocrlf"; "false" ] |> ignore
    run [ "config"; "core.hooksPath"; Path.Combine(fixture, "no-hooks") ] |> ignore
    commit ()
    expectFailure (fun () -> Versioning.sync fixture |> ignore) "Missing tags should fail"

    for invalid in [ "v1.2.3"; "1.2"; "1.2.3-beta"; "01.2.3"; "1.2.3\n"; "-1.2.3" ] do
        expectFailure (fun () -> Versioning.setVersion fixture invalid) $"Accepted invalid version {invalid}"

    check (run [ "status"; "--porcelain" ] = "") "Invalid input changed files"

    let initial = run [ "rev-parse"; "HEAD" ]
    Versioning.setVersion fixture "1.2.3"
    checkVersions "1.2.3"

    check
        (read "nested/package.json" = nested.Replace("0.0.1", "1.2.3"))
        "JSON formatting or dependency versions changed"

    check (run [ "rev-parse"; "HEAD" ] <> initial) "Release did not commit"
    check (run [ "rev-parse"; "HEAD" ] = run [ "rev-parse"; "v1.2.3" ]) "Tag points at wrong commit"
    check (run [ "status"; "--porcelain" ] = "") "Release left tracked changes"
    check (Versioning.displayVersion fixture = "1.2.3") "Exact release description is wrong"
    let beforeDuplicate = snapshot ()
    expectFailure (fun () -> Versioning.setVersion fixture "1.2.3") "Existing tag accepted"
    check (snapshot () = beforeDuplicate) "Duplicate release changed repository state"

    for lower in [ "1.2.2"; "1.1.99"; "0.99.99" ] do
        expectDowngradeRejected lower "1.2.3"

    let timestamp = File.GetLastWriteTimeUtc(Path.Combine(fixture, "package.json"))
    Versioning.sync fixture |> ignore
    check (timestamp = File.GetLastWriteTimeUtc(Path.Combine(fixture, "package.json"))) "Unchanged file was rewritten"
    write "notes.txt" "dirty at tag"
    let dirtyAtTag = Versioning.manifestVersion fixture
    check (Regex.IsMatch(dirtyAtTag, @"^1\.2\.3\+0\.g[0-9a-f]+\.dirty$")) "Dirty exact tag must include hash"
    let beforeVersionRead = snapshot ()
    check (Versioning.displayVersion fixture = "1.2.3+") "Current version must expose tracked changes"
    check (snapshot () = beforeVersionRead) "Reading the current version must not synchronize files or alter Git"
    Versioning.sync fixture |> ignore
    checkVersions dirtyAtTag
    write "notes.txt" "original"
    Versioning.sync fixture |> ignore
    checkVersions "1.2.3"
    check (Versioning.displayVersion fixture = "1.2.3") "Generated metadata made a clean tag dirty"
    write "notes.txt" "next commit"
    commit ()
    run [ "tag"; "unrelated-tag" ] |> ignore
    check (Versioning.gitVersion fixture = "1.2.3") "Unrelated tag was selected"
    check ((Versioning.displayVersion fixture).StartsWith("1.2.3-1-g")) "Commit distance was lost"
    let cleanSnapshot = Versioning.manifestVersion fixture
    check (Regex.IsMatch(cleanSnapshot, @"^1\.2\.3\+1\.g[0-9a-f]+$")) "Snapshot must include distance and hash"
    Versioning.sync fixture |> ignore
    checkVersions "1.2.3"
    check (run [ "status"; "--porcelain" ] = "") "Build after a source commit dirtied a clean checkout"
    // Existing uncommitted version drift can still be synchronized.
    write "package.json" ((read "package.json").Replace("\"version\": \"1.2.3\"", "\"version\": \"9.9.9\""))
    Versioning.sync fixture |> ignore
    checkVersions cleanSnapshot

    let snapshotTimestamp =
        File.GetLastWriteTimeUtc(Path.Combine(fixture, "package.json"))

    Versioning.sync fixture |> ignore
    check (Versioning.manifestVersion fixture = cleanSnapshot) "Version sync caused dirty feedback"

    check
        (snapshotTimestamp = File.GetLastWriteTimeUtc(Path.Combine(fixture, "package.json")))
        "Snapshot sync was not idempotent"

    write "package.json" ((read "package.json").Replace(cleanSnapshot, "9.9.9"))
    write "notes.txt" "dirty"
    let beforeSync = run [ "rev-parse"; "HEAD" ]
    let tagsBeforeSync = run [ "tag"; "--list" ]
    Versioning.sync fixture |> ignore
    checkVersions (cleanSnapshot + ".dirty")
    check ((Versioning.displayVersion fixture).EndsWith("+")) "Dirty suffix was lost"

    check
        (run [ "rev-parse"; "HEAD" ] = beforeSync
         && run [ "tag"; "--list" ] = tagsBeforeSync)
        "Sync created commit/tag"

    check (read "notes.txt" = "dirty") "Sync overwrote unrelated work"
    expectFailure (fun () -> Versioning.setVersion fixture "2.0.0") "Unrelated work accepted for release"
    write "notes.txt" "next commit"
    Versioning.sync fixture |> ignore
    checkVersions cleanSnapshot

    let originalPackage = read "package.json"
    write "package.json" (originalPackage.Replace("\"private\": true", "\"private\": false"))
    run [ "add"; "package.json" ] |> ignore
    write "package.json" originalPackage
    check ((Versioning.manifestVersion fixture).EndsWith(".dirty")) "Hidden staged edits must mark the build dirty"
    expectFailure (fun () -> Versioning.setVersion fixture "2.0.0") "Hidden staged manifest changes accepted"
    run [ "add"; "package.json" ] |> ignore
    // Version-only drift left by a build is safe to include in the release.
    write "package.json" (originalPackage.Replace(cleanSnapshot, "2.0.0"))
    Versioning.setVersion fixture "2.0.0"
    checkVersions "2.0.0"
    check (run [ "status"; "--porcelain" ] = "") "Version-only drift was not committed"

    // Legacy tags without v are also authoritative and cannot be overwritten.
    run [ "tag"; "-d"; "v2.0.0" ] |> ignore
    run [ "tag"; "2.0.0" ] |> ignore
    check (Versioning.gitVersion fixture = "2.0.0") "Legacy tag not supported"
    expectFailure (fun () -> Versioning.setVersion fixture "2.0.0") "Legacy tag collision accepted"
    run [ "tag"; "-d"; "2.0.0" ] |> ignore
    let beforeEmptyRelease = run [ "rev-parse"; "HEAD" ]
    Versioning.setVersion fixture "2.0.0"

    check
        (run [ "rev-parse"; "HEAD" ] <> beforeEmptyRelease)
        "Already-matching manifests must still create a release commit"

    // Reproduce build -> commit (including dirty versions) -> build -> build.
    // The first clean build commits only removal of dirty markers. Subsequent
    // builds preserve the clean strings instead of chasing their own commit hash.
    write "notes.txt" "changes to commit after building"
    let committedVersion = Versioning.sync fixture
    check (committedVersion.EndsWith(".dirty")) "Fixture must commit a dirty version"
    commit ()
    let sourceHead = run [ "rev-parse"; "HEAD" ]
    let committedTags = run [ "tag"; "--list" ]
    // Real edits, including hidden staged changes, must prevent auto-commits.
    write "notes.txt" "staged change"
    run [ "add"; "notes.txt" ] |> ignore
    write "notes.txt" "changes to commit after building"
    Versioning.sync fixture |> ignore
    check (run [ "rev-parse"; "HEAD" ] = sourceHead) "Build committed staged source changes"
    check ((rootVersion "package.json").EndsWith(".dirty")) "Staged source changes lost dirty marker"
    // Restore only the fixture to the clean source commit for the cleanup checks.
    run [ "restore"; "--source=HEAD"; "--staged"; "--worktree"; "." ] |> ignore

    // A failed automatic commit restores the clean checkout and can be retried.
    write ".git/test-hooks/pre-commit" "#!/bin/sh\nexit 1\n"

    run [ "config"; "core.hooksPath"; Path.Combine(fixture, ".git", "test-hooks") ]
    |> ignore

    expectFailure (fun () -> Versioning.sync fixture |> ignore) "Rejected cleanup commit should fail"
    checkVersions committedVersion
    check (run [ "rev-parse"; "HEAD" ] = sourceHead) "Rejected cleanup changed HEAD"
    check (run [ "status"; "--porcelain" ] = "") "Rejected cleanup left a dirty checkout"
    File.Delete(Path.Combine(fixture, ".git", "test-hooks", "pre-commit"))

    Versioning.sync fixture |> ignore

    let cleanVersion =
        committedVersion.Substring(0, committedVersion.Length - ".dirty".Length)

    checkVersions cleanVersion
    let committedHead = run [ "rev-parse"; "HEAD" ]
    check (committedHead <> sourceHead) "Cleanup did not commit"
    check (run [ "rev-parse"; "HEAD^" ] = sourceHead) "Cleanup created more than one commit"
    check (run [ "log"; "-1"; "--format=%s" ] = "Remove dirty markers from project versions") "Wrong cleanup commit"
    let changedPaths = (run [ "diff"; "--name-only"; "HEAD^"; "HEAD" ]).Split('\n')

    for path in changedPaths do
        check
            ([
                "Cargo.toml"
                "Cargo.lock"
                "package.json"
                "package-lock.json"
                "tauri.conf.json"
             ]
             |> List.contains (Path.GetFileName path))
            $"Cleanup committed unrelated file {path}"

        let before = run [ "show"; "HEAD^:" + path ]
        let after = run [ "show"; "HEAD:" + path ]

        check
            (after = before.Replace(committedVersion, cleanVersion))
            $"Cleanup changed more than dirty markers in {path}"

    let committedFiles =
        (run [ "ls-files"; "-z" ]).Split('\000', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun path ->
            path, File.ReadAllBytes(Path.Combine(fixture, path)), File.GetLastWriteTimeUtc(Path.Combine(fixture, path)))

    for _ in 1..2 do
        Versioning.sync fixture |> ignore
        checkVersions cleanVersion
        check (run [ "status"; "--porcelain" ] = "") "Clean build changed the worktree or index"
        check (run [ "rev-parse"; "HEAD" ] = committedHead) "Clean build created a commit"
        check (run [ "tag"; "--list" ] = committedTags) "Clean build changed tags"

        for path, contents, modified in committedFiles do
            check (File.ReadAllBytes(Path.Combine(fixture, path)) = contents) $"Clean build changed {path}"
            check (File.GetLastWriteTimeUtc(Path.Combine(fixture, path)) = modified) $"Clean build rewrote {path}"

    check
        (not ((Versioning.displayVersion fixture).EndsWith("+")))
        "Generated display version should describe the current clean checkout"

    // A legacy higher release remains the floor even after an accidental lower
    // tag on a newer commit. Numeric comparison must handle two-digit components.
    run [ "tag"; "2.10.0" ] |> ignore
    write "notes.txt" "accidental downgrade"
    commit ()
    run [ "tag"; "v2.2.0" ] |> ignore
    check (Versioning.gitVersion fixture = "2.2.0") "Fixture must have a lower nearest release"
    expectDowngradeRejected "2.9.99" "2.10.0"
    expectDowngradeRejected "2.2.1" "2.10.0"
    // Non-release tags do not constrain releases.
    run [ "tag"; "v99.0.0-beta" ] |> ignore
    Versioning.setVersion fixture "2.10.1"
    checkVersions "2.10.1"
    Versioning.setVersion fixture "10.0.0"
    checkVersions "10.0.0"

    let otherHistory =
        run [ "commit-tree"; "HEAD^{tree}"; "-m"; "Release on another history" ]

    run [ "tag"; "v11.0.0"; otherHistory ] |> ignore
    expectDowngradeRejected "10.0.1" "11.0.0"

    printfn "Versioning regression checks passed."
finally
    let resolved = Path.GetFullPath fixture

    if
        not (
            resolved.StartsWith(testDirectory + string Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        )
    then
        failwith "Refusing to delete fixture outside version-tests."
    // Git objects can be read-only on Windows.
    for path in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories) do
        File.SetAttributes(path, FileAttributes.Normal)

    Directory.Delete(resolved, true)

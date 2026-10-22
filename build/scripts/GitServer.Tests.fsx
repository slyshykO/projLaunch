// Run with: dotnet fsi --reference:build/bin/Debug/net11.0/build.dll build/scripts/GitServer.Tests.fsx
open System

let check condition message =
    if not condition then
        failwith message

check (GitServer.parseOptions [] = (false, GitServer.Native)) "Existing native debug command must keep working"

check
    (GitServer.parseOptions [ "--release" ] = (true, GitServer.Native))
    "Existing npm release command must keep working"

check
    (GitServer.parseOptions [ "ubuntu22" ] = (false, GitServer.Ubuntu22 None))
    "Ubuntu target should use the default distribution"

let explicitOptions =
    GitServer.parseOptions [ "ubuntu22"; "--release"; "--wsl-distro"; "Ubuntu Test" ]

check
    (explicitOptions = (true, GitServer.Ubuntu22(Some "Ubuntu Test")))
    "Explicit distro and release options must survive parsing"

for arguments in
    [
        [ "--unknown" ]
        [ "ubuntu22"; "native" ]
        [ "--wsl-distro" ]
        [ "--wsl-distro"; "Ubuntu" ]
        [ "ubuntu22"; "--wsl-distro"; "--release" ]
    ] do
    let rejected =
        try
            GitServer.parseOptions arguments |> ignore
            false
        with _ ->
            true

    check rejected $"Invalid options were accepted: {arguments}"

let calls = ResizeArray<bool * string * string list>()

let linuxScript =
    "/mnt/e/Project With Spaces/build/scripts/build/git-server-ubuntu22.sh"

let run capture command arguments =
    calls.Add(capture, command, arguments)
    if capture then linuxScript + "\n" else ""

if OperatingSystem.IsWindows() then
    GitServerUbuntu22.build run @"E:\Project With Spaces" (Some "Ubuntu Test") true
    check (calls.Count = 2) "WSL must translate the path and then execute the script"

    check
        (calls.[0] =
            (true,
             "wsl.exe",
             [
                 "--distribution"
                 "Ubuntu Test"
                 "--exec"
                 "wslpath"
                 "-a"
                 "-u"
                 @"E:\Project With Spaces\build\scripts\build\git-server-ubuntu22.sh"
             ]))
        "Path conversion must use the same distro and preserve spaces"

    check
        (calls.[1] = (false, "wsl.exe", [ "--distribution"; "Ubuntu Test"; "--exec"; "bash"; linuxScript; "--release" ]))
        "WSL execution must preserve arguments without shell interpolation"

    calls.Clear()
    GitServerUbuntu22.build run @"E:\Project With Spaces" None false

    check
        (calls.[1] = (false, "wsl.exe", [ "--exec"; "bash"; linuxScript ]))
        "Default distro/debug build must omit extra flags"
elif OperatingSystem.IsLinux() then
    GitServerUbuntu22.build run "/project with spaces" None true

    check
        (calls.[0] =
            (false,
             "bash",
             [
                 "/project with spaces/build/scripts/build/git-server-ubuntu22.sh"
                 "--release"
             ]))
        "Linux must run the script directly"

printfn "PASS: Git server build options and process arguments"

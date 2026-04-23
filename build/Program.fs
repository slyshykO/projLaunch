module AppResult =
    [<Literal>]
    let Success = 0

    [<Literal>]
    let Failure = 1

module Args =
    type SubCommand =
        | Build = 0
        | Clean = 1
        | Test = 2
        | Update = 3
        | BuildDaisyUI = 4
        | Help = 5

    let extractSubCommandStr (args: string array) =
        if args.Length = 0 then
            "None"
        else
            args.[0].ToLowerInvariant()

    let subCommand (args: string array) =
        match extractSubCommandStr args with
        | "build" -> Some SubCommand.Build
        | "clean" -> Some SubCommand.Clean
        | "test" -> Some SubCommand.Test
        | "update" -> Some SubCommand.Update
        | "build-daisyui" -> Some SubCommand.BuildDaisyUI
        | "help" -> Some SubCommand.Help
        | _ -> None

module Help =
    let print () =
        Print.println "Usage: build <subcommand>"
        Print.println ""
        Print.println "Available subcommands:"
        Print.println "  build         Build the app"
        Print.println "  clean         Clean generated artifacts"
        Print.println "  test          Reserved for test workflow"
        Print.println "  update        Update dependencies"
        Print.println "  build-daisyui Build local Feliz.DaisyUI"
        Print.println "  help          Show this help"

module Config =

    let isWindows =
        let pID = System.Environment.OSVersion.Platform
        pID = System.PlatformID.Win32NT || pID = System.PlatformID.Win32Windows

    let isUnix =
        let pID = System.Environment.OSVersion.Platform
        pID = System.PlatformID.Unix

    let shell () =
        if isWindows then "cmd.exe" else "/bin/sh"

    let shellCmdArg () = if isWindows then "/C" else "-c"

    let extraPaths () = [||]

module BuildUtils =
    open System
    open System.Diagnostics

    let runCmd (cmd: string) (runArgs: string seq) =
        try
            Print.printBlue cmd

            for arg in runArgs do
                Print.printBlue $" {arg}"

            Print.println ""

            let processInfo = ProcessStartInfo(cmd, runArgs)

            let oldPath =
                Environment.GetEnvironmentVariable "PATH"
                |> ValueOption.ofObj
                |> ValueOption.defaultValue ""

            let envPathSep = if Config.isWindows then ";" else ":"

            processInfo.EnvironmentVariables["PATH"] <-
                String.Join(envPathSep, Array.append (Config.extraPaths ()) [| oldPath |])

            processInfo.UseShellExecute <- false

            use proc = new Process(StartInfo = processInfo)
            proc.Start() |> ignore
            proc.WaitForExit()

            if proc.ExitCode <> 0 then
                Print.printRed "FAILED "
                Print.println $"in {(DateTime.Now - proc.StartTime).TotalSeconds} s"
                AppResult.Failure
            else
                Print.printGreen "Ok "
                Print.println $"in {(DateTime.Now - proc.StartTime).TotalSeconds} s"
                AppResult.Success
        with ex ->
            Print.printlnColor ConsoleColor.Red $"{ex.Message}"
            AppResult.Failure

module State =
    let mutable appResult = AppResult.Success

module Build =

    let private normalizeNugetVersionForJsModulePath (version: string) =
        let split = version.Split([| '-' |], 2, System.StringSplitOptions.None)
        let coreVersion = split.[0]
        let suffix = if split.Length = 2 then $"-{split.[1]}" else ""

        let coreParts =
            coreVersion.Split([| '.' |], System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.toList

        let normalizedCore =
            match coreParts with
            | [ major ] -> $"{major}.0.0"
            | [ major; minor ] -> $"{major}.{minor}.0"
            | _ -> coreVersion

        $"{normalizedCore}{suffix}"

    let private syncFelizDaisyUiSources () =
        try
            let projectRoot =
                System.IO.Path.GetFullPath(System.IO.Path.Combine(__SOURCE_DIRECTORY__, ".."))

            let paketLockPath = System.IO.Path.Combine(projectRoot, "paket.lock")
            let indexCssPath = System.IO.Path.Combine(projectRoot, "src", "index.css")

            if not (System.IO.File.Exists paketLockPath) then
                failwith $"Could not find paket lock file: `{paketLockPath}`"

            if not (System.IO.File.Exists indexCssPath) then
                failwith $"Could not find index.css file: `{indexCssPath}`"

            let paketLockContent = System.IO.File.ReadAllText paketLockPath

            let lockVersionMatch =
                System.Text.RegularExpressions.Regex.Match(
                    paketLockContent,
                    @"^\s*Feliz\.DaisyUI\s+\(([^)]+)\)\s*$",
                    System.Text.RegularExpressions.RegexOptions.Multiline
                )

            if not lockVersionMatch.Success then
                failwith "Could not extract Feliz.DaisyUI version from paket.lock"

            let lockedVersion = lockVersionMatch.Groups.[1].Value.Trim()
            let moduleVersion = normalizeNugetVersionForJsModulePath lockedVersion

            let expectedDaisyUiLine =
                $"@source './bin/fable-js/fable_modules/Feliz.DaisyUI.{moduleVersion}/DaisyUI.fs';"

            let expectedModifiersLine =
                $"@source './bin/fable-js/fable_modules/Feliz.DaisyUI.{moduleVersion}/Modifiers.fs';"

            let cssContent = System.IO.File.ReadAllText indexCssPath

            let daisyUiPattern =
                System.Text.RegularExpressions.Regex(
                    @"@source\s+'\.\/bin\/fable-js\/fable_modules\/Feliz\.DaisyUI\.[^\/']+\/DaisyUI\.fs';"
                )

            let modifiersPattern =
                System.Text.RegularExpressions.Regex(
                    @"@source\s+'\.\/bin\/fable-js\/fable_modules\/Feliz\.DaisyUI\.[^\/']+\/Modifiers\.fs';"
                )

            if not (daisyUiPattern.IsMatch cssContent) then
                failwith "Could not find DaisyUI @source line in src/index.css"

            if not (modifiersPattern.IsMatch cssContent) then
                failwith "Could not find Modifiers @source line in src/index.css"

            let updatedCssContent =
                cssContent
                |> fun content -> daisyUiPattern.Replace(content, expectedDaisyUiLine)
                |> fun content -> modifiersPattern.Replace(content, expectedModifiersLine)

            if updatedCssContent <> cssContent then
                System.IO.File.WriteAllText(indexCssPath, updatedCssContent)
                Print.printlnYellow $"Updated src/index.css to use Feliz.DaisyUI.{moduleVersion}"
            else
                Print.printlnBlue $"Verified src/index.css already uses Feliz.DaisyUI.{moduleVersion}"

            AppResult.Success
        with ex ->
            Print.printlnRed $"Error while syncing Feliz.DaisyUI sources: {ex.Message}"
            AppResult.Failure

    let build () =
        try
            if syncFelizDaisyUiSources () <> AppResult.Success then
                AppResult.Failure
            else
                BuildUtils.runCmd (Config.shell ()) [ Config.shellCmdArg (); "npm"; "run"; "tauri"; "build" ]
        with ex ->
            Print.printlnRed $"Error during build: {ex.Message}"
            AppResult.Failure

    let buildDaisyUI () =
        try
            // print current working directory for debugging
            Print.printlnBlue $"Current working directory: {System.IO.Directory.GetCurrentDirectory()}"
            // Create log file for debugging

            try
                let prjRoot = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "..")
                // find daisyui.sln in the project root and subdirectories
                let daisyUISln =
                    System.IO.Directory.GetFiles(prjRoot, "Feliz.DaisyUI.sln", System.IO.SearchOption.AllDirectories)
                    |> Array.tryHead

                match daisyUISln with
                | Some slnPath ->
                    Print.printlnBlue $"Found solution file at: {slnPath}"

                    let slnDir =
                        match System.IO.Path.GetDirectoryName(slnPath) with
                        | null -> failwith $"Could not get directory name from path: `{slnPath}`"
                        | dir -> dir

                    Print.printlnBlue $"Changing directory to: {slnDir}"
                    System.IO.Directory.SetCurrentDirectory slnDir

                    let daisyUIproj =
                        System.IO.Path.Combine(slnDir, "src", "Feliz.DaisyUI", "Feliz.DaisyUI.fsproj")

                    if BuildUtils.runCmd "dotnet" [ "tool"; "restore" ] <> AppResult.Success then
                        failwith "Failed to restore dotnet tools."

                    if
                        BuildUtils.runCmd "dotnet" [ "build"; "-c"; "Release"; daisyUIproj ]
                        <> AppResult.Success
                    then
                        failwith "Failed to build Feliz.DaisyUI."

                    let nupkgDir = System.IO.Path.Combine(prjRoot, "paket-files", "nupkg")

                    if
                        BuildUtils.runCmd "dotnet" [ "pack"; daisyUIproj; "-p:Version=5.2.99" ]
                        <> AppResult.Success
                    then
                        failwith "Failed to pack Feliz.DaisyUI."

                    AppResult.Success
                | None ->
                    Print.printlnRed "Could not find Feliz.DaisyUI.sln in the project directory or subdirectories."
                    AppResult.Failure
            finally
                1 |> ignore // Placeholder to ensure the finally block is executed
        with ex ->
            Print.printlnRed $"Error during DaisyUI build: {ex.Message}"
            AppResult.Failure

    let update () =
        try
            let npmRes =
                BuildUtils.runCmd (Config.shell ()) [ Config.shellCmdArg (); "npm"; "update" ]

            let cargoRes =
                BuildUtils.runCmd
                    (Config.shell ())
                    [ Config.shellCmdArg ()
                      "cargo"
                      "update"
                      "--manifest-path"
                      "./src-tauri/Cargo.toml" ]

            let dotnetRes =
                if System.IO.File.Exists "paket.dependencies" then
                    BuildUtils.runCmd (Config.shell ()) [ Config.shellCmdArg (); "dotnet"; "paket"; "update" ]
                else
                    BuildUtils.runCmd
                        (Config.shell ())
                        [ Config.shellCmdArg ()
                          "dotnet"
                          "list"
                          "./src/app.fsproj"
                          "package"
                          "--outdated" ]

            npmRes + cargoRes + dotnetRes
        with ex ->
            Print.printlnRed $"Error during update: {ex.Message}"
            AppResult.Failure

    let clean () =
        try
            let cargoRes =
                BuildUtils.runCmd
                    (Config.shell ())
                    [ Config.shellCmdArg ()
                      "cargo"
                      "clean"
                      "--manifest-path"
                      "./src-tauri/Cargo.toml" ]

            cargoRes
        with ex ->
            Print.printlnRed $"Error during clean: {ex.Message}"
            AppResult.Failure

module Main =
    open System

    [<EntryPoint>]
    let main argv =
        let startTime = DateTime.Now

        try
            try
                Print.printlnBlue "Build started..."

                State.appResult <-
                    match Args.subCommand argv with
                    | Some Args.SubCommand.Build -> Build.build ()
                    | Some Args.SubCommand.Update -> Build.update ()
                    | Some Args.SubCommand.Clean -> Build.clean ()
                    | Some Args.SubCommand.BuildDaisyUI -> Build.buildDaisyUI ()
                    | Some Args.SubCommand.Help ->
                        Help.print ()
                        AppResult.Success
                    | _ ->
                        Print.printlnRed $"Unknown subcommand: `{Args.extractSubCommandStr argv}`"
                        Print.println ""
                        Help.print ()

                        AppResult.Failure
            with ex ->
                Print.printlnRed $"Error during build start: {ex.Message}"
                State.appResult <- AppResult.Failure
        finally
            let endTime = DateTime.Now
            let duration = endTime - startTime
            Print.printlnGreen $"Build finished in {duration.TotalSeconds} s."

        State.appResult

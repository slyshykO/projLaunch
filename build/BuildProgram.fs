module AppResult =
    [<Literal>]
    let Success = 0

    [<Literal>]
    let Failure = 1

module Print =

    module private Internal =
        let lockObj = obj ()

        let IsColorEnabled () =
            Gapotchenko.FX.Console.ConsoleTraits.IsColorEnabled

    let print (str: string) =
        lock Internal.lockObj (fun () ->
            System.Console.Write str
            System.Console.Out.Flush())

    let println (str: string) =
        lock Internal.lockObj (fun () ->
            System.Console.WriteLine str
            System.Console.Out.Flush())

    let printColor (color: System.ConsoleColor) (str: string) =
        if Internal.IsColorEnabled() then
            lock Internal.lockObj (fun () ->
                let oldColor = System.Console.ForegroundColor
                System.Console.ForegroundColor <- color
                System.Console.Write str
                System.Console.ForegroundColor <- oldColor
                System.Console.Out.Flush())
        else
            print str

    let printlnColor (color: System.ConsoleColor) (str: string) =
        if Internal.IsColorEnabled() then
            lock Internal.lockObj (fun () ->
                let oldColor = System.Console.ForegroundColor
                System.Console.ForegroundColor <- color
                System.Console.WriteLine str
                System.Console.ForegroundColor <- oldColor
                System.Console.Out.Flush())
        else
            println str

    let printlnRed (str: string) =
        printlnColor System.ConsoleColor.Red str

    let printRed (str: string) = printColor System.ConsoleColor.Red str

    let printlnGreen (str: string) =
        printlnColor System.ConsoleColor.Green str

    let printGreen (str: string) =
        printColor System.ConsoleColor.Green str

    let printlnYellow (str: string) =
        printlnColor System.ConsoleColor.Yellow str

    let printlnBlue (str: string) =
        printlnColor System.ConsoleColor.Blue str

    let printBlue (str: string) = printColor System.ConsoleColor.Blue str

module Args =
    type SubCommand =
        | Build = 0
        | Clean = 1
        | Test = 2
        | Update = 3

    let subCommand (args: string array) =
        if args.Length = 0 then
            None
        else
            match args.[0].ToLowerInvariant() with
            | "build" -> Some SubCommand.Build
            | "clean" -> Some SubCommand.Clean
            | "test" -> Some SubCommand.Test
            | "update" -> Some SubCommand.Update
            | _ -> None

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

    let build () =
        try
            BuildUtils.runCmd (Config.shell ()) [ Config.shellCmdArg (); "npm"; "run"; "tauri"; "build" ]
        with ex ->
            Print.printlnRed $"Error during build: {ex.Message}"
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
                    | _ -> AppResult.Failure
            with ex ->
                Print.printlnRed $"Error during build start: {ex.Message}"
                State.appResult <- AppResult.Failure
        finally
            let endTime = DateTime.Now
            let duration = endTime - startTime
            Print.printlnGreen $"Build finished in {duration.TotalSeconds} s."

        State.appResult

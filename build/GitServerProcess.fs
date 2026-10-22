module GitServerProcess

open System
open System.Diagnostics
open System.IO

let private forwardLines (reader: StreamReader) (writer: TextWriter) =
    task {
        let mutable reading = true

        while reading do
            let! line = reader.ReadLineAsync()

            if isNull line then
                reading <- false
            else
                do! writer.WriteLineAsync line
                do! writer.FlushAsync()
    }

let run workingDirectory captureOutput command (arguments: string list) =
    let info = ProcessStartInfo(command, arguments)
    info.WorkingDirectory <- workingDirectory
    info.UseShellExecute <- false
    info.CreateNoWindow <- true
    // Give windowless children valid pipes instead of inherited console handles.
    // WSL also uses stdin/stderr when only stdout is being captured; inherited
    // handles can be unusable without a console. These commands need no input.
    info.RedirectStandardInput <- true
    info.RedirectStandardOutput <- true
    info.RedirectStandardError <- true
    use proc = new Process(StartInfo = info)
    proc.Start() |> ignore
    proc.StandardInput.Close()

    // Drain both streams concurrently so verbose builds cannot fill a pipe and
    // deadlock. Stream build logs immediately; capture only requested stdout.
    let stderr = forwardLines proc.StandardError Console.Error

    let stdout =
        if captureOutput then
            proc.StandardOutput.ReadToEndAsync()
        else
            task {
                do! forwardLines proc.StandardOutput Console.Out
                return ""
            }

    proc.WaitForExit()
    let output = stdout.GetAwaiter().GetResult()
    stderr.GetAwaiter().GetResult()

    if proc.ExitCode <> 0 then
        if captureOutput && not (String.IsNullOrWhiteSpace output) then
            Console.Error.Write output

        let code = sprintf "%d (0x%08X)" proc.ExitCode (uint32 proc.ExitCode)
        failwith $"{command} exited with code {code}"

    output

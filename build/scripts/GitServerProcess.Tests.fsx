#load "../GitServerProcess.fs"

open System
open System.IO

let check condition message =
    if not condition then
        failwith message

let directory =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "obj", "process-tests"))

Directory.CreateDirectory directory |> ignore
let child = Path.Combine(directory, $"{Guid.NewGuid():N}.fsx")

try
    File.WriteAllText(
        child,
        """open System
if not (isNull (Console.ReadLine())) then failwith "Expected EOF on stdin"
let payload = String.replicate 1024 "x"
for _ in 1..256 do
    Console.Error.WriteLine payload
    Console.Out.WriteLine payload
Console.Out.WriteLine "stdout complete"
Console.Error.WriteLine "stderr complete"
if fsi.CommandLineArgs |> Array.contains "--fail" then Environment.Exit 7
"""
    )

    let originalOut, originalError = Console.Out, Console.Error

    try
        for captureOutput, fail in [ true, false; false, false; true, true ] do
            use stdout = new StringWriter()
            use stderr = new StringWriter()
            Console.SetOut stdout
            Console.SetError stderr

            let arguments = [ "fsi"; "--exec"; child ] @ if fail then [ "--fail" ] else []

            let result =
                try
                    Ok(GitServerProcess.run directory captureOutput "dotnet" arguments)
                with ex ->
                    Error ex.Message

            check (stderr.ToString().Contains "stderr complete") "stderr must be drained while stdout is active"

            match result with
            | Ok output when captureOutput && not fail ->
                check (output.Contains "stdout complete") "Captured stdout must be returned"
                check (stdout.ToString() = "") "Captured stdout must not leak into streamed logs"
            | Ok output when not captureOutput && not fail ->
                check (output = "") "Streamed stdout must not be returned"
                check (stdout.ToString().Contains "stdout complete") "Build output must reach the caller"
            | Error message when fail ->
                check (message.Contains "7 (0x00000007)") "Failures must include decimal and hex exit codes"
                check (stderr.ToString().Contains "stdout complete") "Captured failure diagnostics must be visible"
            | other -> failwith $"Unexpected process result: {other}"
    finally
        Console.SetOut originalOut
        Console.SetError originalError
finally
    File.Delete child

printfn "PASS: process EOF, concurrent stdout/stderr, streamed/captured output and failure diagnostics"

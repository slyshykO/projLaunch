open System
open System.Diagnostics


[<Literal>]
let vf =
    "
//do not edit!
//this file was generated automatically

module Version
[<Literal>]
let version = {0}
"

[<Literal>]
let cf =
    "
//do not edit!
//this file was generated automatically
namespace Version
{{
    public static class Version {{
        public static string version = {0};
    }}
}}
"

let runProc filename args startDir : int * seq<string> * seq<string> =
    let timer = Stopwatch.StartNew()

    let procStartInfo =
        ProcessStartInfo(
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            FileName = filename,
            Arguments = args
        )

    match startDir with
    | Some d -> procStartInfo.WorkingDirectory <- d
    | _ -> ()

    let outputs = Collections.Generic.List<string>()
    let errors = Collections.Generic.List<string>()
    let outputHandler f (_sender: obj) (args: DataReceivedEventArgs) = f args.Data
    use p = new Process(StartInfo = procStartInfo)
    p.OutputDataReceived.AddHandler(DataReceivedEventHandler(outputHandler outputs.Add))
    p.ErrorDataReceived.AddHandler(DataReceivedEventHandler(outputHandler errors.Add))

    let started =
        try
            p.Start()
        with ex ->
            ex.Data.Add("filename", filename)
            reraise ()

    if not started then
        failwithf "Failed to start process %s" filename

    printf "`%s %s` " filename args
    p.BeginOutputReadLine()
    p.BeginErrorReadLine()
    p.WaitForExit()
    timer.Stop()

    if p.ExitCode = 0 then
        printf "OK (%d ms)\n" timer.ElapsedMilliseconds
    else
        printf "FAILED (%d ms)\n" timer.ElapsedMilliseconds

    let cleanOut l =
        l |> Seq.filter (fun s -> String.IsNullOrEmpty s |> not)

    p.ExitCode, cleanOut outputs, cleanOut errors

let verFileDir = fsi.CommandLineArgs.[1]
System.IO.Directory.CreateDirectory(verFileDir) |> ignore
let verFileName = System.IO.Path.Combine(verFileDir, "version.fs")
let mutable version = "\"0.0.1\""

try
    let (res, stdout, stderr) =
        runProc "git" "describe --tags --dirty=+ --always" (Some verFileDir)

    if res = 0 then
        let version =
            sprintf
                "\"%s\""
                (stdout
                 |> Seq.map (fun s -> if s.StartsWith("v") then s.Substring(1) else s)
                 |> Seq.head)

        printfn "Version: %s" version
        let fileContent = System.String.Format(vf, version)
        System.IO.File.WriteAllText(verFileName, fileContent)
        let fileContent = System.String.Format(cf, version)
        System.IO.File.WriteAllText(System.IO.Path.Combine(verFileDir, "Version.cs"), fileContent)
    else
        eprintf "Failed to get version:\n%s" (String.Join("\n", stderr))

    ()
with e ->
    eprintfn "%s" e.Message

#load "Versioning.fs"

open System.IO

if fsi.CommandLineArgs.Length <> 2 then
    failwith "Usage: dotnet fsi --exec scripts/version/version.fsx <output-directory>"

// Versioning.sync Versioning.projectRoot |> ignore
let version = Versioning.displayVersion Versioning.projectRoot
let outputDirectory = fsi.CommandLineArgs.[1]
Directory.CreateDirectory outputDirectory |> ignore

let writeIfChanged name content =
    let path = Path.Combine(outputDirectory, name)

    if not (File.Exists path) || File.ReadAllText path <> content then
        File.WriteAllText(path, content)

// Keep a runtime module reference: a Literal gets inlined into consumers whose
// cached JavaScript may not be rebuilt when only this generated file changes.
writeIfChanged
    "version.fs"
    (sprintf "// Generated automatically. Do not edit.\nmodule Version\nlet version () = \"%s\"\n" version)

writeIfChanged
    "Version.cs"
    (sprintf
        "// Generated automatically. Do not edit.\nnamespace Version\n{\n    public static class Version {\n        public static string version = \"%s\";\n    }\n}\n"
        version)

printfn "Version: %s" version

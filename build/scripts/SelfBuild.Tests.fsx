#load "../SelfBuild.fs"

open System
open System.IO

let testDirectory =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "obj", "self-build-tests"))

let fixture = Path.Combine(testDirectory, Guid.NewGuid().ToString("N"))

let write (relativePath: string) (contents: string) =
    let path = Path.Combine(fixture, relativePath)
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllText(path, contents)

let check condition message =
    if not condition then
        failwith message

try
    write
        "build/build.fsproj"
        """<Project><ItemGroup>
      <Compile Include="obj/hashes.fs" />
      <Compile Include="Program.fs" />
      <ProjectReference Include="../support/support.fsproj" />
    </ItemGroup></Project>"""

    write
        "support/support.fsproj"
        """<Project><ItemGroup>
      <Compile Include="Support.fs" />
    </ItemGroup></Project>"""

    write "build/Program.fs" "module Program"
    write "support/Support.fs" "module Support"
    write "build/scripts/genhashes.fsx" "// generator"
    write "build.bat" "@echo off"
    write "global.json" "{}"
    write "build/packages.lock.json" "{}"
    write "Directory.Build.props" "<Project />"

    let snapshot () =
        SelfBuild.hashStringsForProject (Path.Combine(fixture, "build", "build.fsproj"))

    let original = snapshot ()
    check (snapshot () = original) "Unchanged inputs must produce the same hashes."

    for relativePath in
        [
            "build/Program.fs"
            "support/Support.fs"
            "build/scripts/genhashes.fsx"
            "build.bat"
            "global.json"
            "build/packages.lock.json"
            "Directory.Build.props"
            "build/build.fsproj"
            "support/support.fsproj"
        ] do
        let path = Path.Combine(fixture, relativePath)
        let contents = File.ReadAllBytes path
        let timestamp = File.GetLastWriteTimeUtc path
        File.AppendAllText(path, "\n")
        File.SetLastWriteTimeUtc(path, timestamp)
        check (snapshot () <> original) $"Content changes must invalidate {relativePath}, even with the same timestamp."
        File.WriteAllBytes(path, contents)
        check (snapshot () = original) $"Restoring {relativePath} must restore the hashes."

    write "build/obj/hashes.fs" "module SelfBuildHashes"
    write "src/Unrelated.fs" "module Unrelated"
    check (snapshot () = original) "Generated hashes and application sources must not invalidate the helper."

    write "Directory.Build.targets" "<Project />"
    check (snapshot () <> original) "Adding shared build configuration must invalidate the helper."

    let beforeNewSource = snapshot ()
    write "build/Added.fs" "module Added"

    write
        "build/build.fsproj"
        """<Project><ItemGroup>
      <Compile Include="obj/hashes.fs" />
      <Compile Include="Program.fs" />
      <Compile Include="Added.fs" />
      <ProjectReference Include="../support/support.fsproj" />
    </ItemGroup></Project>"""

    check (snapshot () <> beforeNewSource) "Adding a compile item must invalidate the helper."
    let withNewSource = snapshot ()
    write "build/Added.fs" "module Added\nlet value = 1"
    check (snapshot () <> withNewSource) "New compile items must be tracked."

    File.Delete(Path.Combine(fixture, "build", "Added.fs"))

    let missingSourceDetected =
        try
            snapshot () |> ignore
            false
        with :? FileNotFoundException ->
            true

    check missingSourceDetected "A missing compile input must fail the self-check."

    printfn "Self-build input checks passed."
finally
    let resolvedFixture = Path.GetFullPath fixture

    if
        not (
            resolvedFixture.StartsWith(
                testDirectory + string Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            )
        )
    then
        failwith "Refusing to clean a fixture outside the self-build test directory."

    if Directory.Exists resolvedFixture then
        Directory.Delete(resolvedFixture, true)

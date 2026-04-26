# Serde.FS + Fable Probe Report

Date checked: 2026-04-26

## Summary

`Serde.FS` and `Serde.FS.Json` are still not usable with this Fable probe at version `1.0.0-alpha.14`.

The original failure has changed shape while testing:

1. If the generated Serde codec source is not included in the Fable compilation, Fable emits calls to missing functions such as `Serde_FS_Json_SerdeJson_serialize`, and the generated JavaScript fails at runtime.
2. If the generated Serde codec source is copied out of `obj/` and included in the Fable compilation, Fable sees the codec source but fails to compile it:

```text
SerdeJsonCodecs.g.fs(1,1): error FABLE: Cannot find Serde.FS.Json.Codec.JsonValue constructor
```

So the current status is: generated source inclusion can be made to work, but the generated codec code itself is not Fable-compatible yet.

## Versions

Current locked package versions:

```text
Serde.FS      1.0.0-alpha.14
Serde.FS.Json 1.0.0-alpha.14
Fable         5.0.0
FSharp.Core   11.0.100
```

NuGet flat-container API showed `1.0.0-alpha.14` as the newest available version for both packages when checked:

```text
https://api.nuget.org/v3-flatcontainer/serde.fs/index.json
https://api.nuget.org/v3-flatcontainer/serde.fs.json/index.json
```

Previous locked version was `1.0.0-alpha.11`; the probe was retried after updating to `1.0.0-alpha.14`.

## Probe Shape

Probe project:

```text
serde-probe-lib/serde-probe-lib.fsproj
```

Probe source:

```fsharp
namespace SerdeProbeLib

open Serde.FS
open Serde.FS.Json

[<Serde>]
type ProbeRecord =
    { Name: string
      Count: int }

module Probe =
    let sample =
        { Name = "serde-probe"
          Count = 7 }

    let toJson (value: ProbeRecord) =
        SerdeJson.serialize value

    let fromJson (json: string) =
        SerdeJson.deserialize<ProbeRecord> json

    let sampleJson =
        toJson sample
```

Expected success criteria:

1. Fable compiles `serde-probe-lib`.
2. Node can import the generated `SerdeProbe.js`.
3. `Probe_sampleJson` is valid JSON with `{ "Name": "serde-probe", "Count": 7 }`.
4. `Probe_fromJson` and `Probe_toJson` round-trip the sample.

The current npm script that runs this is:

```text
npm run build:serde-probe
```

It delegates to:

```text
scripts/build-serde-probe.ps1
scripts/check-serde-probe.mjs
```

## Attempt 1: App Build Placeholder

The first script named `build:serde-probe` was only a copy of the normal app build:

```text
dotnet fable src -o ./src/bin/fable-js --run vite build ...
```

That was not a valid probe because `src/Debug.fs` guards the Serde reference behind `#if !FABLE_COMPILER`, so the app build did not actually exercise Serde through Fable.

The script was replaced with a focused probe that compiles `serde-probe-lib` directly and then imports the generated JavaScript with Node.

## Attempt 2: Focused Fable Compile Without Generated Codec Source

Command shape:

```text
dotnet fable serde-probe-lib -o ./serde-probe-lib/bin/fable-js
node ./scripts/check-serde-probe.mjs
```

Fable compiled one source file:

```text
Project and references (1 source files) parsed
```

Generated JavaScript contained missing calls:

```js
export function Probe_toJson(value) {
    return Serde_FS_Json_SerdeJson_serialize(value);
}

export function Probe_fromJson(json) {
    return Serde_FS_Json_SerdeJson_deserialize(json);
}
```

Runtime failure:

```text
ReferenceError: Serde_FS_Json_SerdeJson_serialize is not defined
```

Finding: the Serde source generator does create codec source, but Fable was not compiling it.

## Generated Codec File Found

After a normal .NET build, Serde generated:

```text
serde-probe-lib/obj/Release/net10.0/serde-generated/~SerdeJsonCodecs.json.g.fs
```

That file defines:

```fsharp
namespace Serde.Generated

open Serde.FS.Json
open Serde.FS.Json.Codec

[<AutoOpen>]
module SerdeJsonCodecs =
    ...

type JsonBootstrap() =
    interface Serde.FS.IEntryPointBootstrap with
        member _.Init () =
            Serde.Generated.SerdeJsonCodecs.register
            |> Serde.FS.Json.SerdeJson.registerCodecs
```

Important package target note found in:

```text
.config/.nuget/packages/serde.fs/1.0.0-alpha.14/buildTransitive/Serde.FS.targets
```

The target comments say Fable excludes files in `obj/`, and Serde therefore uses a `generated-fable/` directory for `[<GenerateFableClient>]` output. This plain `[<Serde>]` codec output is still generated under `obj/`, so Fable does not pick it up automatically.

## Attempt 3: Include `obj/` Generated File Directly

Tried adding a project include like:

```xml
<Compile Include="$(IntermediateOutputPath)serde-generated\~SerdeJsonCodecs.json.g.fs"
         Condition="Exists('$(IntermediateOutputPath)serde-generated\~SerdeJsonCodecs.json.g.fs')" />
```

MSBuild could see it in `@(Compile)`, but Fable still reported only one source file and did not include it in `project_cracked.json`.

Finding: direct inclusion from `obj/` is not sufficient for Fable. This matches the package comment that Fable filters `obj/` paths.

## Attempt 4: Copy Generated Source Out Of `obj/`

The probe script now:

1. Deletes any stale copied codec file.
2. Runs `dotnet build serde-probe-lib -c Release`.
3. Copies:

```text
serde-probe-lib/obj/Release/net10.0/serde-generated/~SerdeJsonCodecs.json.g.fs
```

to:

```text
serde-probe-lib/SerdeJsonCodecs.g.fs
```

4. Includes the copied file only for Fable:

```xml
<Compile Include="SerdeProbe.fs" />
<Compile Include="SerdeJsonCodecs.g.fs"
         Condition="Exists('SerdeJsonCodecs.g.fs') and '$(FABLE_COMPILER)' == 'True'" />
```

The file must be listed after `SerdeProbe.fs`, because the generated codec references `SerdeProbeLib.ProbeRecord`.

The copied file is ignored by git:

```text
serde-probe-lib/SerdeJsonCodecs.g.fs
```

This worked mechanically. Fable then reported:

```text
Project and references (2 source files) parsed
```

But compilation failed:

```text
SerdeJsonCodecs.g.fs(1,1): error FABLE: Cannot find Serde.FS.Json.Codec.JsonValue constructor
```

Finding: once Fable sees the generated codec file, it cannot compile the generated Serde JSON codec implementation.

## Current Probe Script Notes

`build-serde-probe.ps1` runs Fable with `--noCache`.

This is important because Fable previously reused stale generated JavaScript and hid changes to source ordering or generated file inclusion.

The script also redirects .NET home/appdata paths into local workspace folders when needed, because the sandboxed environment cannot write to the default user profile directories.

## Current Final Error

Current command:

```text
npm run build:serde-probe
```

Current result:

```text
Build succeeded.
Project and references (2 source files) parsed
Started Fable compilation...
Fable compilation finished

SerdeJsonCodecs.g.fs(1,1): error FABLE: Cannot find Serde.FS.Json.Codec.JsonValue constructor
Compilation failed
```

This is the useful checkpoint for the next retry.

## Conclusion

For `Serde.FS` / `Serde.FS.Json` `1.0.0-alpha.14`:

- Plain .NET build works.
- Fable does not automatically compile the normal generated codec source because it is emitted under `obj/`.
- Copying the generated source out of `obj/` and including it Fable-only makes Fable see the codec source.
- The generated codec source still fails under Fable because `Serde.FS.Json.Codec.JsonValue` constructors are not translated/resolved.

So `Serde.FS` is still incompatible with this plain `[<Serde>]` + `SerdeJson.serialize` / `deserialize` Fable use case.

## Next Retry Checklist

When a newer Serde package is available:

1. Update `Serde.FS` and `Serde.FS.Json`.
2. Run:

```text
npm run build:serde-probe
```

3. Check whether Fable still fails on:

```text
Cannot find Serde.FS.Json.Codec.JsonValue constructor
```

4. If that error is gone, check whether the Node runtime verifier passes.
5. If the generated code moves out of `obj/` or Serde starts generating Fable-specific codecs for `[<Serde>]`, simplify or remove the copy step in `scripts/build-serde-probe.ps1`.

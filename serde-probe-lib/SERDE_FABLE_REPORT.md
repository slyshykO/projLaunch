# Serde.FS + Fable Probe Report

Date checked: 2026-10-19

## Summary

`Serde.FS` / `Serde.FS.Json` **1.0.0-beta.6 still fail this standalone serialization probe with Fable 5.22.0**.

- The .NET Release build succeeds for both `net10.0` and `net11.0`, with zero warnings and errors.
- Fable sees both the probe and the copied generated codec source, but compilation fails.
- The previous `Cannot find Serde.FS.Json.Codec.JsonValue constructor` error remains. Fable also reports DLL-only references for `serialize`, `deserialize`, and codec registry functions.
- The Node import / JSON round-trip verifier is not reached: the script stops on the Fable compilation error.

This result concerns plain `[<Serde>]` records used through `SerdeJson.serialize` / `SerdeJson.deserialize`.

**Follow-up: `Serde.FS.Fable` 1.0.0-beta.6 does compile and run as a generated RPC client with Fable 5.22.0.** The separate probe below passes against mocked HTTP responses, but confirms incompatible JSON shapes and transport for the current application. It is not a direct replacement for our Thoth serializers or Tauri IPC.

## Package Update Check

The live NuGet flat-container indexes were queried on 2026-10-19:

- [Serde.FS versions](https://api.nuget.org/v3-flatcontainer/serde.fs/index.json)
- [Serde.FS.Json versions](https://api.nuget.org/v3-flatcontainer/serde.fs.json/index.json)

Both list `1.0.0-beta.6` as the newest available version, including prereleases. The repository's `paket.lock` already contained that version before this retry. No package version change was needed; the previous report's `1.0.0-alpha.14` was stale. The restored probe's `obj/project.assets.json` also resolves both packages to `1.0.0-beta.6`.

## Tested Versions

| Component | Version |
| --- | --- |
| Serde.FS | 1.0.0-beta.6 |
| Serde.FS.Json | 1.0.0-beta.6 |
| Fable CLI | 5.22.0 |
| FSharp.Core | 11.0.100 |
| .NET SDK | 11.0.100-rc.2.26504.105 |
| Target frameworks | net10.0; net11.0 |
| Node.js | 24.20.0 |
| Paket | 12.0.0-alpha001 |

Fable and Paket are pinned in `.config/dotnet-tools.json`; the SDK is selected by `global.json`. This retry uses the current repository toolchain, so changes in diagnostics cannot be attributed solely to Serde since the April check.

## Probe and Success Criteria

Project: `serde-probe-lib/serde-probe-lib.fsproj`.
Source: `serde-probe-lib/SerdeProbe.fs`.

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

Success requires all of the following:

1. Fable compiles the probe.
2. Node imports the generated `SerdeProbe.js`.
3. `Probe_sampleJson` parses to `{ "Name": "serde-probe", "Count": 7 }`.
4. `Probe_fromJson` and `Probe_toJson` round-trip the sample.

Run:

```powershell
npm run build:serde-probe
```

This invokes `scripts/build-serde-probe.ps1`, followed by `scripts/check-serde-probe.mjs` only if compilation succeeds.

## Latest Execution Result

The script restores tools, builds the .NET probe in Release, copies the generated codec out of `obj/`, and invokes Fable with `--noCache`.

Relevant output, with repeated codec-reference diagnostics omitted:

```text
serde-probe-lib -> ...\bin\Release\net10.0\serde-probe-lib.dll
serde-probe-lib -> ...\bin\Release\net11.0\serde-probe-lib.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Fable 5.22.0: F# to JavaScript compiler
Project and references (2 source files) parsed
Started Fable compilation...
Fable compilation finished

SerdeProbe.fs(17,8): (17,33) error FABLE: Cannot reference member from .dll reference, Fable packages must include F# sources: Serde.FS.Json.SerdeJson.serialize
SerdeProbe.fs(20,8): (20,47) error FABLE: Cannot reference member from .dll reference, Fable packages must include F# sources: Serde.FS.Json.SerdeJson.deserialize
SerdeJsonCodecs.g.fs(15,99): (15,146) error FABLE: Cannot reference member from .dll reference, Fable packages must include F# sources: Serde.FS.Json.Codec.GlobalCodecRegistry.Current
SerdeJsonCodecs.g.fs(15,42): (15,146) error FABLE: Cannot reference member from .dll reference, Fable packages must include F# sources: Serde.FS.Json.Codec.CodecResolver.resolve
SerdeJsonCodecs.g.fs(34,65): (34,104) error FABLE: Cannot reference member from .dll reference, Fable packages must include F# sources: Serde.FS.Json.Codec.JsonCodec.boxCodec
SerdeJsonCodecs.g.fs(34,11): (34,105) error FABLE: Cannot reference member from .dll reference, Fable packages must include F# sources: Serde.FS.Json.Codec.CodecRegistryModule.add
SerdeJsonCodecs.g.fs(42,15): (42,53) error FABLE: Cannot reference member from .dll reference, Fable packages must include F# sources: Serde.FS.Json.SerdeJson.registerCodecs
SerdeJsonCodecs.g.fs(1,1): error FABLE: Cannot find Serde.FS.Json.Codec.JsonValue constructor
Compilation failed
```

Command exit code: **1**. No successful JavaScript runtime test is claimed. The .NET result is a build check, not a .NET serialization runtime check.

The initial sandboxed attempt stopped before compilation on .NET telemetry registry access (`System.Security.SecurityException`). The completed retry used normal profile/network access and these environment settings:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_HOME = 'C:\Users\user'
npm run build:serde-probe
```

That environment failure is separate from the Fable compiler errors above.

## Generated Source and Package Findings

The current .NET JSON generator still emits:

```text
serde-probe-lib/obj/Release/net10.0/serde-generated/~SerdeJsonCodecs.json.g.fs
```

The probe copies this to `serde-probe-lib/SerdeJsonCodecs.g.fs`, an ignored file, and includes it after the record definition only for Fable:

```xml
<Compile Include="SerdeProbe.fs" />
<Compile Include="SerdeJsonCodecs.g.fs"
         Condition="Exists('SerdeJsonCodecs.g.fs') and '$(FABLE_COMPILER)' == 'True'" />
```

Fable reporting two source files confirms that the copied codec is included. Source inclusion alone does not supply the missing runtime implementation: the installed beta.6 packages contain compiled libraries and build targets, but no `fable/` source directory. The generated codec calls those DLL-only runtime members.

The beta.6 targets differ from the old alpha.14 report:

- `Serde.FS/buildTransitive/Serde.FS.targets` handles cleanup of this project's generated artifacts.
- `Serde.FS.Json/buildTransitive/Serde.FS.Json.targets` runs the JSON generator before `CoreCompile` and includes its output from the intermediate directory.
- The JSON targets explicitly state that Fable client generation has moved to the separate `Serde.FS.Fable` package.

## Separate Fable RPC Support

The [upstream README](https://github.com/serde-fs/Serde.FS#readme) documents a different Fable workflow: shared `[<RpcApi>]` interfaces and DTOs, with `Serde.FS.Fable` installed on the consumer project. The generator writes client proxies and matching codecs into the consumer's `fable-generated/` directory and includes them automatically.

[Serde.FS.Fable 1.0.0-beta.6 on NuGet](https://www.nuget.org/packages/Serde.FS.Fable/1.0.0-beta.6) describes the same RPC-specific purpose. The follow-up below tests this package separately. Its RPC support does not make the standalone `SerdeJson.serialize` / `deserialize` API work in Fable.

## Historical Checks

The previous report was dated 2026-04-26 and covered Serde alpha.14 with Fable 5.0.0, after updating from alpha.11:

1. A normal app build was not a valid probe: `src/Debug.fs` excluded Serde calls under `FABLE_COMPILER`.
2. Compiling only `SerdeProbe.fs` produced JavaScript with unresolved Serde calls and a Node `ReferenceError`.
3. Directly including the generated file under `obj/` did not make Fable compile it.
4. Copying the codec out of `obj/` made Fable see two files, but compilation failed on the `JsonValue` constructor.

The current retry retains that focused probe and the copy workaround. It confirms that the standalone scenario remains blocked on beta.6.

## Application Workaround and Next Retry

The conditional reference in `src/app.fsproj` remains in place:

```xml
<ItemGroup Condition="'$(FABLE_COMPILER)' != 'True'">
  <ProjectReference Include="..\serde-probe-lib\serde-probe-lib.fsproj" />
</ItemGroup>
```

The standalone retry did not change application code, build scripts, or dependency versions. The later RPC follow-up adds an isolated probe and an npm command, as described below. The full application build was not rerun; focused probes provide the compatibility results.

For a future retry:

1. Check NuGet and update both Serde packages together if a newer version exists.
2. Run `npm run build:serde-probe` with the restored local toolchain.
3. Require both Fable compilation and the Node round-trip verifier to pass. A .NET build alone is insufficient.
4. Remove the copy step only if the package supplies and includes Fable-compatible source for this standalone scenario.
5. Remove the conditional application reference only after the probe passes, then verify the application build.
6. For RPC, rerun `npm run build:serde-fable-probe` and keep its results distinct from standalone serialization.

## Serde.FS.Fable Follow-up: Executed RPC Probe

Checked on 2026-10-19 with `Serde.FS.Fable` 1.0.0-beta.6, `Serde.FS` 1.0.0-beta.6, `Fable.Core` 5.4.0, `FSharp.Core` 11.0.100, Fable 5.22.0 and Node 24.20.0. The isolated projects target `net10.0` and use explicit NuGet `PackageReference` entries; the application's Paket dependencies are unchanged.

Files:

- `serde-fable-probe/Shared/Domain.fs`: a simple record plus data shapes mirroring `src/ProjectData.fs`, exposed through `[<RpcApi>] IProbeApi`.
- `serde-fable-probe/Shared/Shared.fsproj`: shared contracts with `Serde.FS`.
- `serde-fable-probe/Client/Client.fsproj`: consumer with `Serde.FS.Fable` and `Fable.Core`.
- `serde-fable-probe/Client/Probe.fs`: invokes generated methods and checks decoded values.
- `scripts/build-serde-fable-probe.ps1`: tool restore, .NET build, uncached Fable compilation, then Node verification.
- `scripts/check-serde-fable-probe.mjs`: checks HTTP requests against expected JSON and supplies independent response fixtures through a mock `fetch`.

Run:

```powershell
npm run build:serde-fable-probe
```

The .NET build generates `Client/fable-generated/~IProbeApi.fable.g.fs` and includes it before the consumer's source. No manual codec copy or project-file include is needed. The generated folder contains a self-ignoring `.gitignore`. Fable parses three source files: shared contracts, generated client, and probe.

Final result: **exit code 0**, .NET build with zero warnings/errors, Fable compilation successful, and:

```text
PASS: generated RPC record encoder, HTTP request and decoder
PASS: ProjectData-shaped DateTime, Map and Remote option (None/Wsl/Ssh)
CONFIRMED INCOMPATIBILITY: current environment JSON object silently decodes as empty Map
CONFIRMED INCOMPATIBILITY: current externally tagged Remote is rejected
PASS: HTTP failure propagation
Serde.FS.Fable probe passed; current application wire format is incompatible.
```

Seven intercepted requests cover a simple record, all three remote cases, two incompatible application-shaped responses, and an HTTP 500 response. These are real executions of Fable-generated JavaScript, with mocked transport. No ASP.NET server, Rust integration, browser/webview, authentication, or CORS behavior was tested.

### Fit for projLaunch

| Concern | Current application | Generated Serde.FS.Fable client |
| --- | --- | --- |
| Transport | Tauri `invoke` in `src/AppTauri.fs`; Rust handles Git JSON-RPC | `fetch` POST to `/rpc/IProbeApi/Echo` or `/rpc/IProbeApi/EchoProject`, with raw argument JSON |
| `environment` | JSON object, e.g. `{"EXTRA":"1"}` | Array of pairs, e.g. `[["EXTRA","1"]]` |
| `Remote.Wsl` | `{"Wsl":"Ubuntu"}` | `{"Case":"Wsl","Fields":["Ubuntu"]}` |
| `Remote.Ssh` | `{"Ssh":{"host":"example.test","username":"user"}}` | `{"Case":"Ssh","Fields":["example.test","user"]}` |
| Optional remote | `null` or omitted; legacy Thoth arrays also accepted | Generated decoder handles nullish values; union values use `Case` / `Fields` |
| Codec access | Explicit public `ProjectData.toJson/fromJson` | Per-type codec modules are `private` inside the generated RPC client |

The application formats were checked against `src/ProjectData.fs` and `src-tauri/src/project.rs`. The generated formats were checked both in source and in intercepted requests.

Passing our current environment object to the generated decoder does **not** raise a decoder error: under this toolchain it produces an empty Map. The probe's value comparison detects that data loss. Independently, passing `{"Wsl":"Ubuntu"}` with an otherwise valid generated payload fails with `Unknown union case`.

Date handling also needs care: the generated decoder uses `DateTime.Parse`, while our existing Thoth decoder uses `Decode.datetimeUtc`. In the tested Europe/Kiev environment, a UTC timestamp came back as local time. The positive probe normalizes `lastOpened` with `ToUniversalTime()` before structural comparison; it verifies the instant, not preservation of `DateTime.Kind` or the original timestamp text.

### Decision

We can use this package for a new HTTP RPC boundary that follows the generated contract. Our simple record and representative application data shapes work with its own wire format, subject to the date normalization above.

We should not replace `ProjectData.toJson/fromJson`, existing persisted JSON, or Tauri/Git transport with it directly. That would require explicit transport integration and format adapters or a deliberate data migration. The tested package generates private codecs tied to RPC methods; it does not supply a public general-purpose Fable `SerdeJson.serialize/deserialize` replacement.

Keep the current Thoth implementation for production. The separate probe provides a reproducible baseline for evaluating a future RPC integration without changing application behavior.

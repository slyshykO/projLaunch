$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Invoke-ProbeCommand($file, [string[]] $arguments) {
    & $file @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$file failed with exit code $LASTEXITCODE"
    }
}

Push-Location $root
try {
    Invoke-ProbeCommand "dotnet" @("tool", "restore")
    # Generation runs during .NET build; Fable then consumes fable-generated/.
    Invoke-ProbeCommand "dotnet" @("build", "serde-fable-probe/Client/Client.fsproj", "-c", "Release", "-v", "minimal")
    Invoke-ProbeCommand "dotnet" @("fable", "serde-fable-probe/Client", "-o", "./serde-fable-probe/Client/bin/fable-js", "--noCache")
    Invoke-ProbeCommand "node" @("./scripts/check-serde-fable-probe.mjs")
} finally {
    Pop-Location
}

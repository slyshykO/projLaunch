$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$dotnetHome = Join-Path $root ".config"
$appDataRoot = Join-Path $root ".appdata"
$probeProject = Join-Path $root "serde-probe-lib\serde-probe-lib.fsproj"
$generatedCodecSource = Join-Path $root "serde-probe-lib\obj\Release\net10.0\serde-generated\~SerdeJsonCodecs.json.g.fs"
$fableCodecSource = Join-Path $root "serde-probe-lib\SerdeJsonCodecs.g.fs"

function Test-WritableDirectory($path) {
    try {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
        $testFile = Join-Path $path ".serde-probe-write-test"
        Set-Content -LiteralPath $testFile -Value "" -NoNewline
        Remove-Item -LiteralPath $testFile -Force
        return $true
    } catch {
        return $false
    }
}

function Invoke-NativeCommand($file, [string[]] $arguments) {
    & $file @arguments

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if (-not $env:DOTNET_CLI_HOME -or -not (Test-WritableDirectory $env:DOTNET_CLI_HOME)) {
    $env:DOTNET_CLI_HOME = $dotnetHome
}

try {
    $nugetConfig = Join-Path $env:APPDATA "NuGet\NuGet.Config"
    if (Test-Path -LiteralPath $nugetConfig) {
        Get-Item -LiteralPath $nugetConfig | Out-Null
    }
} catch {
    $env:APPDATA = Join-Path $appDataRoot "Roaming"
    $env:LOCALAPPDATA = Join-Path $appDataRoot "Local"
    New-Item -ItemType Directory -Force -Path $env:APPDATA, $env:LOCALAPPDATA | Out-Null
}

Push-Location $root
try {
    Invoke-NativeCommand "dotnet" @("tool", "restore")

    if (Test-Path -LiteralPath $fableCodecSource) {
        Remove-Item -LiteralPath $fableCodecSource -Force
    }

    Invoke-NativeCommand "dotnet" @("build", $probeProject, "-c", "Release", "-v", "minimal")

    if (-not (Test-Path -LiteralPath $generatedCodecSource)) {
        Write-Error "Expected generated Serde codec file was not found: $generatedCodecSource"
    }

    Copy-Item -LiteralPath $generatedCodecSource -Destination $fableCodecSource -Force
    Invoke-NativeCommand "dotnet" @("fable", "serde-probe-lib", "-o", "./serde-probe-lib/bin/fable-js", "--noCache")
    Invoke-NativeCommand "node" @("./scripts/check-serde-probe.mjs")
} finally {
    Pop-Location
}

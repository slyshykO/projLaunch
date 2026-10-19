$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot ('target/clean-tests-' + [guid]::NewGuid().ToString('N'))))
$fixture = Join-Path $testRoot 'repo with spaces'
$outside = Join-Path $testRoot 'outside'
$links = [System.Collections.Generic.List[string]]::new()

function Write-Fixture([string] $RelativePath, [string] $Content = 'fixture') {
    $path = Join-Path $fixture $RelativePath
    [void] [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path))
    [System.IO.File]::WriteAllText($path, $Content)
}

function Assert([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Invoke-Clean {
    Push-Location $outside
    try {
        # Use the actual public entry point from another working directory.
        & (Join-Path $fixture 'build.bat') clean | Out-Host
        return $LASTEXITCODE
    } finally {
        Pop-Location
    }
}

function New-Junction([string] $RelativePath, [string] $Destination) {
    $path = Join-Path $fixture $RelativePath
    [void] [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path))
    New-Item -ItemType Junction -Path $path -Target $Destination | Out-Null
    $links.Add($path)
}

try {
    [void] [System.IO.Directory]::CreateDirectory($outside)
    Write-Fixture 'scripts/placeholder'
    Copy-Item -LiteralPath (Join-Path $projectRoot 'build.bat') -Destination (Join-Path $fixture 'build.bat')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../clean.ps1') -Destination (Join-Path $fixture 'scripts/clean.ps1')

    $preserved = @('package.json', 'package-lock.json', 'Cargo.lock', '.git/config',
        'src/index.css', 'src/Source.fs', 'src-tauri/src/main.rs', 'src-tauri/Cargo.lock',
        'src-tauri/icons/icon.ico', 'src-tauri/gen/android/keep.txt',
        'node_modules/library/bin/tool.js', 'node_modules/library/package.json',
        'node_modules/library/dist/keep.js', '.paket/Paket.Restore.targets',
        '.config/dotnet-tools.json', '.config/.nuget/packages/keep.txt',
        '.appdata/Roaming/com.projlaunch.app/projlaunch.db', 'docs/bin/keep.txt',
        'paket-files/github.com/slyshykO/Feliz.DaisyUI/src/Feliz.DaisyUI/Source.fs')

    $dotnetProjects = @('build/build.fsproj', 'src/app.fsproj', 'src-test/src-test.fsproj',
        'serde-probe-lib/serde-probe-lib.fsproj', 'serde-fable-probe/Shared/Shared.fsproj',
        'serde-fable-probe/Client/Client.fsproj', 'nested/new project/new.csproj',
        'paket-files/github.com/slyshykO/Feliz.DaisyUI/Build.fsproj',
        'paket-files/github.com/slyshykO/Feliz.DaisyUI/src/Feliz.DaisyUI/Feliz.DaisyUI.fsproj',
        'paket-files/github.com/slyshykO/Feliz.DaisyUI/src/Docs/Docs.fsproj')
    $cargoProjects = @('src-tauri/Cargo.toml', 'crates/Cargo.toml',
        'crates/git-server/Cargo.toml', 'crates/git-client/Cargo.toml', 'crates/git-protocol/Cargo.toml')
    $preserved += $dotnetProjects + $cargoProjects
    foreach ($path in $preserved) { Write-Fixture $path }

    $removed = @('target/custom-target/release/output', 'bin/Release/output', 'obj/cache',
        'TestResults/result.trx', '.fake/cache', 'dist/index.html', 'dist-ssr/server.js',
        'node_modules/.vite/cache', 'node_modules/.vite-temp/cache',
        'src-tauri/binaries/project-launch-git-server-x86_64-pc-windows-msvc.exe',
        'src-tauri/binaries/project-launch-git-server-aarch64-unknown-linux-gnu',
        'src-tauri/gen/schemas/desktop-schema.json', 'serde-probe-lib/SerdeJsonCodecs.g.fs',
        'paket-files/nupkg/library.nupkg', 'paket-files/github.com/slyshykO/Feliz.DaisyUI/publish/docs/index.html')
    foreach ($project in $dotnetProjects) {
        $directory = Split-Path $project -Parent
        $removed += @('bin/Debug/net10.0/app.dll', 'bin/Release/net11.0/win-x64/publish/app.exe',
            'bin/fable-js/fable_modules/runtime.js', 'obj/Debug/cache', 'obj/Release/cache',
            'TestResults/result.trx', 'fable-generated/codec.fs', '.fable-build/output.js') |
            ForEach-Object { "$directory/$_" }
    }
    foreach ($project in $cargoProjects) {
        $directory = Split-Path $project -Parent
        $removed += @('target/debug/app.exe', 'target/release/bundle/app.msi',
            'target/x86_64-pc-windows-msvc/debug/server.exe', 'target/aarch64-unknown-linux-gnu/custom/server') |
            ForEach-Object { "$directory/$_" }
    }
    foreach ($path in $removed) { Write-Fixture $path }
    (Get-Item -LiteralPath (Join-Path $fixture 'dist/index.html')).IsReadOnly = $true

    # A linked project must not be discovered, and a junction inside an output
    # tree must be removed without deleting the directory it points to.
    [System.IO.File]::WriteAllText((Join-Path $outside 'external.fsproj'), 'external')
    [void] [System.IO.Directory]::CreateDirectory((Join-Path $outside 'bin'))
    $sentinel = Join-Path $outside 'bin/keep.txt'
    [System.IO.File]::WriteAllText($sentinel, 'outside')
    New-Junction 'linked-project' $outside
    New-Junction 'src/bin/external' $outside

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'scripts/clean.ps1') -WhatIf
    Assert ($LASTEXITCODE -eq 0) 'Preview failed.'
    foreach ($path in $removed) {
        Assert (Test-Path -LiteralPath (Join-Path $fixture $path)) "Preview removed $path"
    }

    $exitCode = Invoke-Clean
    Assert ($exitCode -eq 0) 'Batch clean failed.'
    foreach ($path in $removed) {
        Assert (-not (Test-Path -LiteralPath (Join-Path $fixture $path))) "Artifact remains: $path"
    }
    foreach ($path in $preserved) {
        Assert ([System.IO.File]::ReadAllText((Join-Path $fixture $path)) -eq 'fixture') "Preserved file changed: $path"
    }
    Assert ([System.IO.File]::ReadAllText($sentinel) -eq 'outside') 'Clean followed a junction.'
    Assert (Test-Path -LiteralPath (Join-Path $fixture 'linked-project')) 'Linked source project was removed.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $fixture 'build/bin'))) 'Helper output was not removed.'
    Assert ((Invoke-Clean) -eq 0) 'Repeated clean should succeed without rebuilding the helper.'

    # A locked output must fail the command, while independent trees are cleaned.
    Write-Fixture 'dist/locked.txt'
    Write-Fixture 'src-test/bin/Debug/after-lock.dll'
    $locked = [System.IO.File]::Open((Join-Path $fixture 'dist/locked.txt'), 'Open', 'ReadWrite', 'None')
    try {
        $exitCode = Invoke-Clean
        Assert ($exitCode -ne 0) 'Locked files must result in a nonzero exit code.'
        Assert (-not (Test-Path -LiteralPath (Join-Path $fixture 'src-test/bin'))) 'Clean stopped after the first failure.'
    } finally {
        $locked.Dispose()
    }
    Assert ((Invoke-Clean) -eq 0) 'Retry after releasing a locked file failed.'

    # Even an explicitly listed output must not be deleted through a linked parent.
    [void] [System.IO.Directory]::CreateDirectory((Join-Path $outside 'schemas'))
    $schemaSentinel = Join-Path $outside 'schemas/keep.json'
    [System.IO.File]::WriteAllText($schemaSentinel, 'outside')
    $generated = [System.IO.Path]::GetFullPath((Join-Path $fixture 'src-tauri/gen'))
    Assert ($generated.StartsWith($fixture + '\', [System.StringComparison]::OrdinalIgnoreCase)) 'Invalid fixture path.'
    Remove-Item -LiteralPath $generated -Recurse -Force
    New-Junction 'src-tauri/gen' $outside
    Assert ((Invoke-Clean) -ne 0) 'A linked artifact ancestor must be rejected.'
    Assert ([System.IO.File]::ReadAllText($schemaSentinel) -eq 'outside') 'Clean removed an output through a linked ancestor.'

    Write-Host 'Clean regression checks passed.'
} finally {
    # Remove the fixture links themselves before recursively removing test data.
    foreach ($link in $links) {
        $resolved = [System.IO.Path]::GetFullPath($link)
        Assert ($resolved.StartsWith($fixture + '\', [System.StringComparison]::OrdinalIgnoreCase)) 'Invalid fixture link.'
        if (Test-Path -LiteralPath $resolved) { [System.IO.Directory]::Delete($resolved) }
    }
    $resolved = [System.IO.Path]::GetFullPath($testRoot)
    $testPrefix = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'target')) + '\'
    Assert ($resolved.StartsWith($testPrefix, [System.StringComparison]::OrdinalIgnoreCase)) 'Invalid test directory.'
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

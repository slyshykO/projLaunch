[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rootPrefix = $projectRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
$artifacts = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$failures = [System.Collections.Generic.List[string]]::new()

function Add-Artifact([string] $Directory, [string] $Name) {
    $path = [System.IO.Path]::GetFullPath((Join-Path $Directory $Name))
    if (-not $path.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean outside the repository: $path"
    }
    [void] $artifacts.Add($path)
}

# Discover projects without walking dependencies, build outputs or directory links.
# Paket source checkouts are included because build-daisyui builds projects there.
$outputNames = @('bin', 'obj', 'target', 'dist', 'dist-ssr', 'TestResults',
    '.fable', '.fable-build', 'fable_modules', 'fable-generated', '.fake', '.vite', '.vite-temp')
$skipNames = $outputNames + @('.git', '.hg', '.svn', '.vs', '.idea', 'node_modules',
    '.paket', '.config', '.appdata', '.codex', '.agents', '.aws', 'nupkg')

function Find-ProjectArtifacts([string] $Directory) {
    $entries = @(Get-ChildItem -LiteralPath $Directory -Force)
    $files = @($entries | Where-Object { -not $_.PSIsContainer } | ForEach-Object { $_.Name })
    if (@($files | Where-Object { $_ -match '\.(fs|cs|vb)proj$' }).Count -gt 0) {
        foreach ($name in @('bin', 'obj', 'TestResults', '.fable', '.fable-build', 'fable_modules', 'fable-generated', '.fake')) {
            Add-Artifact $Directory $name
        }
    }
    if ($files -contains 'Cargo.toml') {
        Add-Artifact $Directory 'target'
    }
    if ($files -contains 'package.json') {
        foreach ($name in @('dist', 'dist-ssr', '.vite', '.vite-temp', 'node_modules/.vite', 'node_modules/.vite-temp')) {
            Add-Artifact $Directory $name
        }
    }
    foreach ($entry in $entries) {
        if ($entry.PSIsContainer -and $entry.Name -notin $skipNames -and
            -not ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            Find-ProjectArtifacts $entry.FullName
        }
    }
}

# These outputs are shared, copied outside the usual output tree, or generated
# by Tauri/probe scripts. Removing whole output trees covers every profile,
# framework, runtime identifier, Rust target triple and publish/bundle variant.
foreach ($name in @('bin', 'obj', 'target', 'dist', 'dist-ssr', 'TestResults', '.fake',
    'src-tauri/binaries', 'src-tauri/gen/schemas',
    'serde-probe-lib/SerdeJsonCodecs.g.fs',
    'paket-files/nupkg', 'paket-files/github.com/slyshykO/Feliz.DaisyUI/publish')) {
    Add-Artifact $projectRoot $name
}
Find-ProjectArtifacts $projectRoot

function Assert-LocalPath([string] $Path) {
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean outside the repository: $resolved"
    }
    # A lexical prefix alone is insufficient if an ancestor is a junction.
    $parent = [System.IO.Path]::GetDirectoryName($resolved)
    while ($parent -ne $projectRoot) {
        $item = Get-Item -LiteralPath $parent -Force
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            throw "Refusing to clean through a directory link: $parent"
        }
        $parent = [System.IO.Path]::GetDirectoryName($parent)
    }
}

function Remove-Artifact([System.IO.FileSystemInfo] $Item) {
    if ($Item.PSIsContainer) {
        if (-not ($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            # Walk explicitly: Windows PowerShell's recursive deletion can follow
            # junctions. Directory.Delete without recursion only removes the link.
            $children = @(Get-ChildItem -LiteralPath $Item.FullName -Force)
            foreach ($child in ($children | Where-Object { $_.PSIsContainer })) {
                Remove-Artifact $child
            }
            $files = @($children | Where-Object { -not $_.PSIsContainer } | ForEach-Object { $_.FullName })
            if ($files.Count -gt 0) {
                Remove-Item -LiteralPath $files -Force
            }
        }
        [System.IO.Directory]::Delete($Item.FullName)
    } else {
        Remove-Item -LiteralPath $Item.FullName -Force
    }
}

foreach ($path in ($artifacts | Sort-Object)) {
    try {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        Assert-LocalPath $path
        if ($PSCmdlet.ShouldProcess($path, 'Remove generated build artifacts')) {
            Write-Host "Cleaning $($path.Substring($rootPrefix.Length))"
            Remove-Artifact (Get-Item -LiteralPath $path -Force)
        }
    } catch {
        $failures.Add($path)
        Write-Warning "Could not clean ${path}: $($_.Exception.Message)"
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Clean failed for $($failures.Count) path(s). Close processes using these files and retry."
    exit 1
}
Write-Host 'Clean completed.'

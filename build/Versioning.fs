module Versioning

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

let projectRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let private git root (args: string list) =
    let info = ProcessStartInfo("git", args)
    info.WorkingDirectory <- root
    info.UseShellExecute <- false
    info.RedirectStandardOutput <- true
    info.RedirectStandardError <- true
    use proc = new Process(StartInfo = info)
    proc.Start() |> ignore
    let stdout = proc.StandardOutput.ReadToEndAsync()
    let stderr = proc.StandardError.ReadToEndAsync()
    proc.WaitForExit()
    let output = stdout.GetAwaiter().GetResult()
    let error = stderr.GetAwaiter().GetResult()

    if proc.ExitCode <> 0 then
        let command = String.concat " " args
        failwith $"git {command} failed: {error.Trim()}"

    output.TrimEnd('\r', '\n')

let private releasePattern =
    @"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)"

let validateVersion (version: string) =
    if not (Regex.IsMatch(version, "\\A" + releasePattern + "\\z")) then
        invalidArg (nameof version) "Expected a release version X.Y.Z (for example, 0.4.2)."

let private tags root =
    (git root [ "tag"; "--list" ]).Split('\n', StringSplitOptions.RemoveEmptyEntries)

let private releaseVersionFromTag tag =
    let matched = Regex.Match(tag, "\\Av?(" + releasePattern + ")\\z")

    if matched.Success then
        Some matched.Groups.[1].Value
    else
        None

let private versionNumbers (version: string) =
    version.Split('.')
    |> Array.map (fun part -> Numerics.BigInteger.Parse(part, Globalization.CultureInfo.InvariantCulture))

let private describe root options =
    let releaseTags = tags root |> Array.filter (releaseVersionFromTag >> Option.isSome)

    if Array.isEmpty releaseTags then
        failwith "No release tag found. Run build.bat set-version X.Y.Z first."

    let matches =
        releaseTags |> Array.collect (fun tag -> [| "--match"; tag |]) |> Array.toList

    git root ([ "describe"; "--tags" ] @ matches @ options)

let gitVersion root =
    let tag = describe root [ "--abbrev=0" ]

    if tag.StartsWith("v", StringComparison.Ordinal) then
        tag.Substring(1)
    else
        tag

// Replace only selected JSON string tokens, preserving formatting and dependencies.
let private updateJson isLock (transformVersion: string -> string) (content: string) =
    let bytes = Encoding.UTF8.GetBytes content
    let mutable reader = Utf8JsonReader(ReadOnlySpan<byte>(bytes))
    let mutable path: string list = []
    let mutable property = "$"
    let edits = ResizeArray<int * int * string>()

    while reader.Read() do
        match reader.TokenType with
        | JsonTokenType.PropertyName -> property <- reader.GetString() |> Option.ofObj |> Option.defaultValue ""
        | JsonTokenType.StartObject ->
            path <- property :: path
            property <- ""
        | JsonTokenType.StartArray -> path <- "[]" :: path
        | JsonTokenType.EndObject
        | JsonTokenType.EndArray -> path <- List.tail path
        | JsonTokenType.String when
            property = "version"
            && (path = [ "$" ] || (isLock && path = [ ""; "packages"; "$" ]))
            ->
            let version = reader.GetString() |> Option.ofObj |> Option.defaultValue ""
            edits.Add(int reader.TokenStartIndex, int reader.BytesConsumed, transformVersion version)
        | _ -> ()

    if edits.Count <> (if isLock then 2 else 1) then
        failwith "Expected a root version (and packages[\"\"].version in package-lock.json)."

    let mutable result = content

    for startOffset, endOffset, version in edits |> Seq.rev do
        let start = Encoding.UTF8.GetCharCount(bytes, 0, startOffset)
        let length = Encoding.UTF8.GetCharCount(bytes, startOffset, endOffset - startOffset)
        result <- result.Remove(start, length).Insert(start, "\"" + version + "\"")

    result

let private sections = Regex(@"(?m)^\[[^\r\n]+\][^\r\n]*(?:\r?\n|$)")

let private versionLine =
    Regex("(?m)^(version[ \\t]*=[ \\t]*[\"'])([^\"'\\r\\n]+)([\"'])")

let private updateSections (update: string -> string -> string) (content: string) =
    let headers = sections.Matches content
    let mutable result = content

    for i in [ headers.Count - 1 .. -1 .. 0 ] do
        let header = headers.[i]
        let start = header.Index + header.Length

        let finish =
            if i + 1 < headers.Count then
                headers.[i + 1].Index
            else
                content.Length

        let body = content.Substring(start, finish - start)
        let replacement = update (header.Value.Trim()) body
        result <- result.Remove(start, finish - start).Insert(start, replacement)

    result

let private replaceVersion transformVersion body =
    if versionLine.Matches(body).Count <> 1 then
        failwith "Expected exactly one explicit version in TOML package section."

    versionLine.Replace(
        body,
        MatchEvaluator(fun m -> m.Groups.[1].Value + transformVersion m.Groups.[2].Value + m.Groups.[3].Value)
    )

let private updateCargo version content =
    updateSections
        (fun header body ->
            if header = "[workspace.package]" || header = "[package]" then
                if Regex.IsMatch(body, @"(?m)^version\.workspace\s*=\s*true\s*$") then
                    body
                else
                    replaceVersion version body
            else
                body)
        content

let private packageName body =
    let m = Regex.Match(body, "(?m)^name\\s*=\\s*\"([^\"]+)\"")
    if m.Success then Some m.Groups.[1].Value else None

let private updateCargoLock localNames version content =
    updateSections
        (fun header body ->
            if
                header = "[[package]]"
                && not (Regex.IsMatch(body, @"(?m)^source\s*="))
                && (packageName body |> Option.exists (fun name -> Set.contains name localNames))
            then
                replaceVersion version body
            else
                body)
        content

let private trackedFiles root =
    (git root [ "ls-files"; "-z" ]).Split('\000', StringSplitOptions.RemoveEmptyEntries)
    |> Array.toList

let private transformationsWith root transformVersion =
    let files = trackedFiles root

    let cargoFiles =
        files |> List.filter (fun path -> Path.GetFileName path = "Cargo.toml")

    let localNames =
        cargoFiles
        |> List.choose (fun path -> File.ReadAllText(Path.Combine(root, path)) |> packageName)
        |> Set.ofList

    files
    |> List.choose (fun path ->
        let update =
            match Path.GetFileName path with
            | "package.json"
            | "tauri.conf.json" -> Some(updateJson false transformVersion)
            | "package-lock.json" -> Some(updateJson true transformVersion)
            | "Cargo.toml" -> Some(updateCargo transformVersion)
            | "Cargo.lock" -> Some(updateCargoLock localNames transformVersion)
            | _ -> None

        update |> Option.map (fun transform -> path, transform))

let private transformations root version =
    transformationsWith root (fun _ -> version)

let private dirtyPaths root =
    git root [ "diff"; "HEAD"; "--name-only"; "-z" ]
    + git root [ "diff"; "--cached"; "--name-only"; "-z" ]
    |> fun output -> output.Split('\000', StringSplitOptions.RemoveEmptyEntries)
    |> Array.toList
    |> List.distinct

let private hasNonVersionChanges root transforms path =
    match transforms |> List.tryFind (fun (candidate, _) -> candidate = path) with
    | Some(_, transform) when File.Exists(Path.Combine(root, path)) ->
        try
            let normalize (text: string) =
                text.Replace("\r\n", "\n").TrimEnd('\n')

            let committed = git root [ "show"; "HEAD:" + path ] |> transform |> normalize
            let staged = git root [ "show"; ":" + path ] |> transform |> normalize
            let current = File.ReadAllText(Path.Combine(root, path)) |> transform |> normalize
            committed <> current || committed <> staged
        with _ ->
            true // Added/deleted or invalid files are real working-tree changes.
    | _ -> true

let private versionDetails root =
    // --long also gives the commit ID on an exact tag, needed for dirty builds.
    let description = describe root [ "--long" ]

    let parts =
        Regex.Match(description, "^v?(" + releasePattern + @")-([0-9]+)-(g[0-9a-f]+)$")

    if not parts.Success then
        failwith $"Unexpected Git version: {description}"

    let version, distance, hash =
        parts.Groups.[1].Value, parts.Groups.[2].Value, parts.Groups.[3].Value

    let transforms = transformations root version
    // Synchronizing generated version fields must not itself make a build dirty.
    // Compare HEAD, index and working tree with just those fields normalized.
    let dirty = dirtyPaths root |> List.exists (hasNonVersionChanges root transforms)

    let manifest =
        if distance = "0" && not dirty then
            version
        else
            version + "+" + distance + "." + hash + (if dirty then ".dirty" else "")

    let display =
        version
        + (if distance = "0" then "" else "-" + distance + "-" + hash)
        + (if dirty then "+" else "")

    manifest, display

let manifestVersion root = versionDetails root |> fst
let displayVersion root = versionDetails root |> snd

let private changes root transformations =
    transformations
    |> List.choose (fun (path, transform) ->
        let original = File.ReadAllText(Path.Combine(root, path))

        let updated =
            try
                transform original
            with ex ->
                failwith $"Cannot update {path}: {ex.Message}"

        if original = updated then None else Some(path, updated))

let private apply root (edits: (string * string) list) =
    for path, content in edits do
        File.WriteAllText(Path.Combine(root, path), content)
        Console.WriteLine $"Updated {path}"

let private cleanDirtyVersion (version: string) =
    if Regex.IsMatch(version, "\\A" + releasePattern + @"\+[0-9]+\.g[0-9a-f]+\.dirty\z") then
        version.Substring(0, version.Length - ".dirty".Length)
    else
        version

let private commitCleanVersions root edits =
    git root [ "var"; "GIT_AUTHOR_IDENT" ] |> ignore
    git root [ "var"; "GIT_COMMITTER_IDENT" ] |> ignore
    let head = git root [ "rev-parse"; "HEAD" ]

    let originals =
        edits
        |> List.map (fun (path, content) ->
            path, File.ReadAllBytes(Path.Combine(root, path)), Encoding.UTF8.GetBytes(content: string))

    try
        apply root edits
        let paths = edits |> List.map fst
        // --only commits just these tracked files without staging anything else.
        git
            root
            ([ "commit"; "--only"; "-m"; "Remove dirty markers from project versions"; "--" ]
             @ paths)
        |> Console.WriteLine
    with ex ->
        // A rejected commit (e.g. a hook) must not strand the checkout with clean
        // versions that were never committed. Preserve any intervening user edits.
        if git root [ "rev-parse"; "HEAD" ] = head then
            for path, original, written in originals do
                let fullPath = Path.Combine(root, path)

                if File.Exists fullPath && File.ReadAllBytes fullPath = written then
                    File.WriteAllBytes(fullPath, original)

        failwith $"Could not commit clean project versions: {ex.Message}"

let sync root =
    let version = manifestVersion root
    // A commit changes its own distance/hash. Never rewrite committed manifests
    // just because a clean checkout now describes a newer commit.
    if List.isEmpty (dirtyPaths root) then
        let cleanEdits = changes root (transformationsWith root cleanDirtyVersion)

        if List.isEmpty cleanEdits then
            Console.WriteLine $"Git is clean; keeping committed manifest versions (current Git version: {version})."
        else
            commitCleanVersions root cleanEdits
            Console.WriteLine "Committed clean project versions; no tag created."
    else
        let edits = changes root (transformations root version)
        apply root edits
        Console.WriteLine $"Versions synchronized with Git: {version}"

    version

let setVersion root version =
    validateVersion version
    let tag = "v" + version
    let existingTags = tags root

    if
        existingTags
        |> Array.exists (fun existing -> existing = tag || existing = version)
    then
        failwith $"Release tag {tag} or {version} already exists."

    // Use every known release tag, not just the nearest tag on this branch:
    // an earlier accidental downgrade must not allow another lower release.
    let releases = existingTags |> Array.choose releaseVersionFromTag

    if not (Array.isEmpty releases) then
        let latest = releases |> Array.maxBy versionNumbers

        if versionNumbers version <= versionNumbers latest then
            failwith $"Release version {version} must be greater than the latest release {latest}."

    git root [ "rev-parse"; "--verify"; "HEAD" ] |> ignore
    git root [ "var"; "GIT_AUTHOR_IDENT" ] |> ignore
    git root [ "var"; "GIT_COMMITTER_IDENT" ] |> ignore
    let transforms = transformations root version
    // Build synchronization may already have changed versions. Accept only those
    // changes, never silently include other edits (even in a manifest) in a release.
    let dirtyPaths = dirtyPaths root

    for path in dirtyPaths do
        if hasNonVersionChanges root transforms path then
            failwith $"Commit or stash non-version changes in {path} before set-version."

    let edits = changes root transforms
    apply root edits
    let paths = (edits |> List.map fst) @ dirtyPaths |> List.distinct

    if not (List.isEmpty paths) then
        git root ([ "add"; "--" ] @ paths) |> ignore

    git root ([ "commit"; "--only"; "--allow-empty"; "-m"; "Release " + tag; "--" ] @ paths)
    |> Console.WriteLine

    git root [ "tag"; tag ] |> ignore
    Console.WriteLine $"Created release commit and tag {tag}"

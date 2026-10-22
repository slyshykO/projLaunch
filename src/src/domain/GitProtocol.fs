module GitProtocol

/// Missing endpoint means local execution, including workspace snapshots saved before WSL support.
type Endpoint =
    {
        kind: string
        distribution: string option
    }

let endpointKey (endpoint: Endpoint option) =
    match endpoint with
    | Some value when value.kind = "wsl" -> "wsl:" + (value.distribution |> Option.defaultValue "")
    | _ -> "local"

let endpointLabel (endpoint: Endpoint option) =
    match endpoint with
    | Some value when value.kind = "wsl" -> "WSL: " + (value.distribution |> Option.defaultValue "")
    | _ -> "Local"

let repositoryLabel endpoint name =
    match endpoint with
    | Some value when value.kind = "wsl" -> name + " [" + endpointLabel endpoint + "]"
    | _ -> name

type RepositoryStatus =
    {
        root: string
        endpoint: Endpoint option
        name: string
        branch: string option
        dirty: bool option
        error: string option
    }

type ProjectStatus =
    {
        repositories: RepositoryStatus array
        warnings: string array
        unsupported: bool
    }

type Hello =
    {
        serverVersion: string
        protocolVersion: int
        gitVersion: string
        capabilities: string array
    }

type Repository =
    {
        repoId: string
        root: string
        gitDir: string
        commonDir: string
    }

type StatusEntry =
    {
        path: string
        originalPath: string option
        index: string
        worktree: string
        kind: string
        submodule: string
    }

type Status =
    {
        head: string option
        branch: string option
        upstream: string option
        ahead: int
        behind: int
        entries: StatusEntry array
    }

type Branch =
    {
        name: string
        refName: string
        oid: string
        isRemote: bool
        isCurrent: bool
        upstream: string option
        tracking: string option
    }

type BranchList =
    {
        branches: Branch array
        head: string option
        currentBranch: string option
    }

type Commit =
    {
        oid: string
        parents: string array
        authorName: string
        authorEmail: string
        authorDate: string
        commitDate: string
        subject: string
        refs: string array
    }

type LogPage =
    {
        commits: Commit array
        nextCursor: string option
    }

type DiffTarget =
    | Unstaged
    | Staged
    | Commit of oid: string * parent: int option
    | Untracked

type DiffFile =
    {
        path: string
        originalPath: string option
        status: string
    }

type Patch =
    {
        path: string
        text: string option
        binary: bool
        truncated: bool
        untracked: bool
    }

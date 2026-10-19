module GitProtocol

type RepositoryStatus =
    {
        root: string
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

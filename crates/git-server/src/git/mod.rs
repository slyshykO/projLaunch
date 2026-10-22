mod branches;
mod diff;
mod history;
pub mod runner;
mod status;

use anyhow::{Context, Result, ensure};
use git_protocol::{Repository, Status};
pub use history::{Cursor, History};
use runner::{GitRunner, args};
use std::{
    path::{Path, PathBuf},
    sync::Arc,
};
use tokio_util::sync::CancellationToken;

#[derive(Clone)]
pub struct Repo {
    pub root: PathBuf,
    pub info: Repository,
    pub runner: GitRunner,
}

pub async fn open(path: &str, runner: GitRunner, cancel: &CancellationToken) -> Result<Arc<Repo>> {
    let directory = Path::new(path);
    ensure!(
        directory.is_absolute(),
        "Repository path must be absolute: '{}'",
        directory.display()
    );
    let inside = runner
        .run(
            directory,
            &args(&["rev-parse", "--is-inside-work-tree"]),
            cancel,
        )
        .await?;
    ensure!(
        line(&inside)? == "true",
        "A working tree is required (bare repositories are not supported): '{}'",
        directory.display()
    );
    let root = runner
        .run(
            directory,
            &args(&["rev-parse", "--path-format=absolute", "--show-toplevel"]),
            cancel,
        )
        .await?;
    let git_dir = runner
        .run(
            directory,
            &args(&["rev-parse", "--absolute-git-dir"]),
            cancel,
        )
        .await?;
    let common = runner
        .run(
            directory,
            &args(&["rev-parse", "--path-format=absolute", "--git-common-dir"]),
            cancel,
        )
        .await?;
    let info = Repository {
        repo_id: uuid::Uuid::new_v4().to_string(),
        root: line(&root)?.into(),
        git_dir: line(&git_dir)?.into(),
        common_dir: line(&common)?.into(),
    };
    Ok(Arc::new(Repo {
        root: PathBuf::from(&info.root),
        info,
        runner,
    }))
}

pub fn line(bytes: &[u8]) -> Result<&str> {
    let value =
        std::str::from_utf8(bytes).context("Git paths must be valid UTF-8 in protocol v1")?;
    // Git writes LF even on Windows. Preserve whitespace belonging to a path.
    Ok(value.strip_suffix('\n').unwrap_or(value))
}

impl Repo {
    pub async fn status(&self, cancel: &CancellationToken) -> Result<Status> {
        status::parse(
            &self
                .runner
                .run(
                    &self.root,
                    &args(&[
                        "status",
                        "--porcelain=v2",
                        "--branch",
                        "--ahead-behind",
                        "--ignore-submodules=none",
                        "-z",
                        "--untracked-files=all",
                        "--renames",
                    ]),
                    cancel,
                )
                .await?,
        )
    }

    pub async fn head(&self, cancel: &CancellationToken) -> Result<Option<String>> {
        // Porcelain handles unborn and detached HEAD without hiding command failures.
        Ok(self.status(cancel).await?.head)
    }

    pub async fn resolve_commit(&self, oid: &str, cancel: &CancellationToken) -> Result<String> {
        ensure!(
            (oid.len() == 40 || oid.len() == 64) && oid.bytes().all(|b| b.is_ascii_hexdigit()),
            "Expected a full commit OID"
        );
        let bytes = self
            .runner
            .run(
                &self.root,
                &args(&["rev-parse", "--verify", &format!("{oid}^{{commit}}")]),
                cancel,
            )
            .await?;
        Ok(line(&bytes)?.into())
    }
}

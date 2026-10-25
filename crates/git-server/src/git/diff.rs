use super::{Repo, line, runner::args};
use anyhow::{Context, Result, ensure};
use git_protocol::{DiffFile, DiffTarget, MAX_PATCH, Patch};
use std::path::{Component, Path};
use tokio::io::AsyncReadExt;
use tokio_util::sync::CancellationToken;

impl Repo {
    async fn diff_args(
        &self,
        target: &DiffTarget,
        cancel: &CancellationToken,
    ) -> Result<Vec<String>> {
        let mut result = args(&[
            "diff",
            "--no-ext-diff",
            "--no-textconv",
            "--no-color",
            "--ignore-submodules=none",
            "--find-renames",
        ]);
        match target {
            DiffTarget::Unstaged => {}
            // Git itself compares against the empty tree for an unborn HEAD.
            DiffTarget::Staged => result.push("--cached".into()),
            DiffTarget::Commit { oid, parent } => {
                let oid = self.resolve_commit(oid, cancel).await?;
                let bytes = self
                    .runner
                    .run(
                        &self.root,
                        &args(&["rev-list", "--parents", "-n", "1", &oid]),
                        cancel,
                    )
                    .await?;
                let parents: Vec<_> = line(&bytes)?.split_whitespace().skip(1).collect();
                ensure!(
                    parents.len() <= 1 || parent.is_some(),
                    "Merge diff requires an explicit one-based parent"
                );
                let base = if parents.is_empty() {
                    ensure!(parent.is_none(), "Root commit has no parent");
                    // Hash the empty tree using the repository's object format (SHA-1 or SHA-256).
                    let empty = self
                        .runner
                        .run(
                            &self.root,
                            &args(&["hash-object", "-t", "tree", "--stdin"]),
                            cancel,
                        )
                        .await?;
                    line(&empty)?.to_owned()
                } else {
                    let index = parent.unwrap_or(1);
                    ensure!(
                        index >= 1 && index <= parents.len(),
                        "Parent index is out of range"
                    );
                    parents[index - 1].to_owned()
                };
                result.extend([base, oid]);
            }
            DiffTarget::Untracked => anyhow::bail!("Untracked previews do not use git diff"),
        }
        Ok(result)
    }

    pub async fn diff_files(
        &self,
        target: &DiffTarget,
        cancel: &CancellationToken,
    ) -> Result<Vec<DiffFile>> {
        if matches!(target, DiffTarget::Untracked) {
            return Ok(self
                .status(cancel)
                .await?
                .entries
                .into_iter()
                .filter(|e| e.kind == "untracked")
                .map(|e| DiffFile {
                    path: e.path,
                    original_path: None,
                    status: "?".into(),
                })
                .collect());
        }
        let mut command = self.diff_args(target, cancel).await?;
        command.extend(args(&["--name-status", "-z", "--"]));
        let bytes = self.runner.run(&self.root, &command, cancel).await?;
        let mut fields = std::str::from_utf8(&bytes)
            .context("Git paths must be valid UTF-8 in protocol v1")?
            .split('\0')
            .filter(|s| !s.is_empty());
        let mut files = vec![];
        while let Some(status) = fields.next() {
            let first = fields.next().context("Missing diff path")?;
            let renamed = status.starts_with('R') || status.starts_with('C');
            let path = if renamed {
                fields.next().context("Missing rename destination")?
            } else {
                first
            };
            files.push(DiffFile {
                path: path.into(),
                original_path: renamed.then(|| first.into()),
                status: status.into(),
            });
        }
        Ok(files)
    }

    pub async fn patch(
        &self,
        target: &DiffTarget,
        path: &str,
        cancel: &CancellationToken,
    ) -> Result<Patch> {
        self.patch_file(target, path, cancel)
            .await
            .map_err(|error| {
                // Cancellation is a protocol sentinel, not a file access failure.
                if error.to_string() == "Operation cancelled" {
                    error
                } else {
                    error.context(format!(
                        "Cannot read diff for '{}'",
                        self.root.join(path).display()
                    ))
                }
            })
    }

    async fn patch_file(
        &self,
        target: &DiffTarget,
        path: &str,
        cancel: &CancellationToken,
    ) -> Result<Patch> {
        validate_path(path)?;
        // Resolve rename source from Git, never trust a client-supplied second path.
        let files = self.diff_files(target, cancel).await?;
        let file = files
            .iter()
            .find(|f| f.path == path)
            .context("Path is not in the selected diff; refresh the file list")?;
        if matches!(target, DiffTarget::Untracked) {
            return self.preview(path, cancel).await;
        }
        let mut command = self.diff_args(target, cancel).await?;
        command.extend(args(&[
            "--patch",
            "--src-prefix=a/",
            "--dst-prefix=b/",
            "--unified=3",
            "--",
        ]));
        if let Some(old) = &file.original_path {
            command.push(old.clone());
        }
        command.push(path.into());
        let output = self
            .runner
            .output(&self.root, &command, cancel, MAX_PATCH, true, false)
            .await?;
        let text = String::from_utf8_lossy(&output.bytes).into_owned();
        let binary = text
            .lines()
            .any(|line| line.starts_with("Binary files ") || line == "GIT binary patch");
        Ok(Patch {
            path: path.into(),
            text: Some(text),
            binary,
            truncated: output.truncated,
            untracked: false,
        })
    }

    async fn preview(&self, path: &str, cancel: &CancellationToken) -> Result<Patch> {
        let full = self.root.join(path);
        let metadata = tokio::fs::symlink_metadata(&full)
            .await
            .with_context(|| format!("Cannot inspect '{}'", full.display()))?;
        let (bytes, truncated) = if metadata.file_type().is_symlink() {
            // Display the link target, do not follow it outside the worktree.
            (
                tokio::fs::read_link(&full)
                    .await
                    .with_context(|| format!("Cannot read symbolic link '{}'", full.display()))?
                    .to_string_lossy()
                    .as_bytes()
                    .to_vec(),
                false,
            )
        } else {
            ensure!(
                metadata.is_file(),
                "Only regular files and symbolic links can be previewed"
            );
            let resolved = tokio::fs::canonicalize(&full)
                .await
                .with_context(|| format!("Cannot resolve preview path '{}'", full.display()))?;
            let root = tokio::fs::canonicalize(&self.root).await.with_context(|| {
                format!("Cannot resolve working tree '{}'", self.root.display())
            })?;
            ensure!(
                resolved.starts_with(&root),
                "Preview path escapes the working tree"
            );
            let mut bytes = Vec::new();
            tokio::select! {
                _ = cancel.cancelled() => anyhow::bail!("Operation cancelled"),
                result = tokio::time::timeout(self.runner.timeout, async {
                    tokio::fs::File::open(&resolved)
                        .await
                        .with_context(|| format!("Cannot open preview file '{}'", resolved.display()))?
                        .take((MAX_PATCH + 1) as u64)
                        .read_to_end(&mut bytes)
                        .await
                        .with_context(|| format!("Cannot read preview file '{}'", resolved.display()))
                }) => { result.with_context(|| format!("Preview timed out for '{}'", resolved.display()))??; },
            }
            let truncated = bytes.len() > MAX_PATCH;
            bytes.truncate(MAX_PATCH);
            (bytes, truncated)
        };
        let binary = bytes.contains(&0)
            || std::str::from_utf8(&bytes).is_err_and(|e| !truncated || e.error_len().is_some());
        Ok(Patch {
            path: path.into(),
            text: (!binary).then(|| String::from_utf8_lossy(&bytes).into_owned()),
            binary,
            truncated,
            untracked: true,
        })
    }
}

pub(super) fn validate_path(path: &str) -> Result<()> {
    ensure!(!path.is_empty() && !path.contains('\0'), "Invalid path");
    ensure!(
        Path::new(path)
            .components()
            .all(|c| matches!(c, Component::Normal(_))),
        "Expected a relative file path without parent components"
    );
    Ok(())
}

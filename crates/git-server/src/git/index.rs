use super::{Repo, diff::validate_path, runner::args};
use anyhow::{Context, Result, ensure};
use git_protocol::DiffTarget;
use std::collections::BTreeSet;
use tokio_util::sync::CancellationToken;

impl Repo {
    pub async fn change_index(
        &self,
        stage: bool,
        target: &DiffTarget,
        paths: &[String],
        cancel: &CancellationToken,
    ) -> Result<bool> {
        ensure!(
            matches!(
                (stage, target),
                (true, DiffTarget::Unstaged | DiffTarget::Untracked) | (false, DiffTarget::Staged)
            ),
            "Stage requires unstaged/untracked files; unstage requires staged files"
        );
        // Never let an empty pathspec turn into an operation on the entire index.
        ensure!(
            !paths.is_empty() && paths.len() <= 10000,
            "Select 1–10000 files"
        );
        let files = self.diff_files(target, cancel).await?;
        let mut selected = BTreeSet::new();
        for path in paths {
            validate_path(path)?;
            let file = files
                .iter()
                .find(|file| file.path == *path)
                .with_context(|| {
                    format!("'{path}' is no longer in the selected group; refresh changes")
                })?;
            selected.insert(path.clone());
            // A rename is one UI row but two index paths. A copy keeps its source.
            if file.status.starts_with('R')
                && let Some(original) = &file.original_path
            {
                validate_path(original)?;
                selected.insert(original.clone());
            }
        }
        let status = self.status(cancel).await?;
        // Literal Git pathspecs still match descendants. Do not silently include
        // other changed files when a file has been replaced by a directory.
        for entry in &status.entries {
            for candidate in std::iter::once(&entry.path).chain(entry.original_path.iter()) {
                ensure!(
                    selected.contains(candidate)
                        || !selected
                            .iter()
                            .any(|path| candidate.starts_with(&format!("{path}/"))),
                    "Selection overlaps other changes under '{candidate}'; select those files together"
                );
            }
        }
        if stage {
            for path in &selected {
                if let Ok(metadata) = tokio::fs::symlink_metadata(self.root.join(path)).await {
                    ensure!(
                        !metadata.is_dir()
                            || status.entries.iter().any(
                                |entry| entry.path == *path && entry.submodule.starts_with('S')
                            ),
                        "'{path}' is a directory; select its individual files"
                    );
                }
            }
        }
        let input: Vec<u8> = selected
            .iter()
            .flat_map(|path| path.bytes().chain([0]))
            .collect();
        let command = if stage {
            args(&[
                "add",
                "--all",
                "--pathspec-from-file=-",
                "--pathspec-file-nul",
            ])
        } else {
            // The path form changes only the index, including for an unborn HEAD.
            // No reset mode (especially --hard) and no worktree restoration.
            args(&[
                "reset",
                "--quiet",
                "--pathspec-from-file=-",
                "--pathspec-file-nul",
            ])
        };
        // One Git process / index.lock transaction for the whole selection. Never
        // retry a mutation after a timeout/disconnect: its outcome may be unknown.
        self.runner
            .run_with_input(&self.root, &command, &input, cancel)
            .await?;
        Ok(true)
    }
}

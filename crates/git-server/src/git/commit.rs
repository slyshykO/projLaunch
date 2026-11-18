use super::{Repo, line, runner::args};
use anyhow::{Result, ensure};
use git_protocol::{CommitParams, CommitResult, DiffTarget};
use std::path::Path;
use tokio_util::sync::CancellationToken;

impl Repo {
    pub async fn commit(
        &self,
        params: &CommitParams,
        cancel: &CancellationToken,
    ) -> Result<CommitResult> {
        ensure!(
            !params.message.trim().is_empty()
                && !params.message.contains('\0')
                && params.message.len() <= 65536,
            "Commit message must contain text, contain no NUL, and fit in 65536 UTF-8 bytes"
        );
        // This action creates ordinary commits. Continuing integration operations
        // requires separate UI that explains their different parent/history semantics.
        for marker in [
            "MERGE_HEAD",
            "CHERRY_PICK_HEAD",
            "REVERT_HEAD",
            "rebase-merge",
            "rebase-apply",
            "sequencer",
        ] {
            ensure!(
                !Path::new(&self.info.git_dir).join(marker).try_exists()?,
                "Repository operation in progress ({marker}); finish or abort it with Git before creating a commit"
            );
        }
        let status = self.status(cancel).await?;
        ensure!(
            status.head == params.expected_head && status.branch == params.expected_branch,
            "HEAD or branch changed; review refreshed changes before committing"
        );
        ensure!(
            status.entries.iter().all(|entry| entry.kind != "conflict"),
            "Resolve all conflicts before creating a commit"
        );
        ensure!(
            !self
                .diff_files(&DiffTarget::Staged, cancel)
                .await?
                .is_empty(),
            "No staged changes to commit"
        );
        // No shell, pathspec, -a, --amend or hook/signing bypass. Message travels
        // through stdin and cannot become a command argument. Git owns index locks.
        self.runner
            .run_with_input(
                &self.root,
                &args(&["commit", "--no-edit", "--cleanup=whitespace", "--file=-"]),
                params.message.as_bytes(),
                cancel,
            )
            .await?;
        let head = self
            .runner
            .run(
                &self.root,
                &args(&["rev-parse", "--verify", "HEAD"]),
                cancel,
            )
            .await?;
        Ok(CommitResult {
            oid: line(&head)?.into(),
        })
    }
}

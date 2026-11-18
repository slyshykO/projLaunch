use super::{Repo, runner::args};
use anyhow::{Context, Result, ensure};
use git_protocol::{DiffTarget, GeneratedCommitMessage};
use tokio_util::sync::CancellationToken;

const CONTEXT_LIMIT: usize = 256 * 1024;

impl Repo {
    pub async fn commit_message_context(
        &self,
        cancel: &CancellationToken,
    ) -> Result<(String, String)> {
        let staged = self.diff_files(&DiffTarget::Staged, cancel).await?;
        let scope = if staged.is_empty() { "all" } else { "staged" };
        let target = if staged.is_empty() {
            DiffTarget::Unstaged
        } else {
            DiffTarget::Staged
        };
        let mut command = self.diff_args(&target, cancel).await?;
        command.extend(args(&[
            "--patch",
            "--src-prefix=a/",
            "--dst-prefix=b/",
            "--unified=3",
            "--",
        ]));
        let output = self
            .runner
            .output(&self.root, &command, cancel, CONTEXT_LIMIT, false, false)
            .await
            .context("Diff is too large or unavailable; stage a smaller set of changes")?;
        let mut context = String::from_utf8_lossy(&output.bytes).into_owned();
        if scope == "all" {
            for file in self.diff_files(&DiffTarget::Untracked, cancel).await? {
                let patch = self.preview(&file.path, cancel).await?;
                ensure!(
                    !patch.truncated || patch.binary,
                    "Untracked file {:?} is too large; stage a smaller set of changes",
                    file.path
                );
                context.push_str(&format!(
                    "\nUntracked file {}:\n",
                    serde_json::to_string(&file.path)?
                ));
                context.push_str(
                    patch
                        .text
                        .as_deref()
                        .unwrap_or("[Binary file; contents omitted]"),
                );
                ensure!(
                    context.len() <= CONTEXT_LIMIT,
                    "Diff exceeds 256 KiB; stage a smaller set of changes"
                );
            }
        }
        ensure!(!context.trim().is_empty(), "No changes to describe");
        Ok((scope.into(), context))
    }

    pub async fn generate_commit_message(
        &self,
        agent: git_protocol::CommitAgent,
        cancel: &CancellationToken,
    ) -> Result<GeneratedCommitMessage> {
        let (scope, diff) = self.commit_message_context(cancel).await?;
        let message = crate::commit_agent::generate(agent, &scope, &diff, cancel).await?;
        // A result must describe the same snapshot, even if a watcher is unavailable.
        let (current_scope, current_diff) = self.commit_message_context(cancel).await?;
        ensure!(
            scope == current_scope && diff == current_diff,
            "Repository changed during generation; generate a new message after reviewing the changes"
        );
        Ok(GeneratedCommitMessage { message, scope })
    }
}

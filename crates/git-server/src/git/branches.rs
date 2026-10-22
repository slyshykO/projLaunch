use super::{Repo, runner::args};
use anyhow::{Context, Result, ensure};
use git_protocol::{Branch, BranchList};
use tokio_util::sync::CancellationToken;

impl Repo {
    /// Read all branch tips, independently of history pagination. Symbolic remote
    /// aliases (usually origin/HEAD) are not additional branches.
    pub async fn branches(&self, cancel: &CancellationToken) -> Result<BranchList> {
        let bytes = self.runner.run(&self.root, &args(&[
            "for-each-ref", "--sort=refname",
            "--format=%(refname)%00%(objectname)%00%(symref)%00%(upstream:short)%00%(upstream:track,nobracket)",
            "refs/heads", "refs/remotes",
        ]), cancel).await?;
        let status = self.status(cancel).await?;
        let mut branches = Vec::new();
        for record in std::str::from_utf8(&bytes)
            .context("Branch names must be valid UTF-8")?
            .lines()
        {
            let fields: Vec<_> = record.split('\0').collect();
            ensure!(fields.len() == 5, "Invalid branch record");
            if !fields[2].is_empty() {
                continue;
            }
            let (name, is_remote) = match fields[0].strip_prefix("refs/heads/") {
                Some(name) => (name, false),
                None => (
                    fields[0]
                        .strip_prefix("refs/remotes/")
                        .context("Invalid branch ref")?,
                    true,
                ),
            };
            branches.push(Branch {
                name: name.into(),
                ref_name: fields[0].into(),
                oid: fields[1].into(),
                is_remote,
                is_current: !is_remote && status.branch.as_deref() == Some(name),
                upstream: (!fields[3].is_empty()).then(|| fields[3].into()),
                tracking: (!fields[4].is_empty()).then(|| fields[4].into()),
            });
        }
        Ok(BranchList {
            branches,
            head: status.head,
            current_branch: status.branch,
        })
    }
}

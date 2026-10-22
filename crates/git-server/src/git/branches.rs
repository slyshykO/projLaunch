use super::{Repo, runner::args};
use anyhow::{Context, Result, ensure};
use git_protocol::{Branch, BranchList, Remote};
use std::collections::BTreeMap;
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
            remotes: self.remotes(cancel).await?,
            head: status.head,
            current_branch: status.branch,
        })
    }

    async fn remotes(&self, cancel: &CancellationToken) -> Result<Vec<Remote>> {
        // NUL framing preserves spaces and newlines in configured URLs. Git
        // exits with 1 when no remote URL is configured, which is normal.
        let output = self
            .runner
            .output(
                &self.root,
                &args(&["config", "--null", "--get-regexp", r"^remote\..*\.url$"]),
                cancel,
                super::runner::OUTPUT_LIMIT,
                false,
                true,
            )
            .await?;
        let mut urls = BTreeMap::new();
        for record in std::str::from_utf8(&output.bytes)
            .context("Remote URLs must be valid UTF-8")?
            .split_terminator('\0')
        {
            let (key, url) = record
                .split_once('\n')
                .context("Invalid remote URL record")?;
            let name = key
                .strip_prefix("remote.")
                .and_then(|key| key.strip_suffix(".url"))
                .context("Invalid remote URL key")?;
            if url.is_empty() {
                urls.remove(name);
            } else {
                // Git fetch uses the first URL; later values are push alternatives.
                urls.entry(name).or_insert(url);
            }
        }
        Ok(urls
            .into_iter()
            .map(|(name, url)| Remote {
                name: name.into(),
                url: url.into(),
            })
            .collect())
    }
}

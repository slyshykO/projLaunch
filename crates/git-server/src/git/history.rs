use super::{Repo, line, runner::args};
use anyhow::{Context, Result, ensure};
use git_protocol::{Commit, LogPage};
use std::{collections::BTreeMap, sync::Arc, time::Instant};
use tokio_util::sync::CancellationToken;

pub struct History {
    pub repo_id: String,
    pub created: Instant,
    roots: Vec<String>,
    refs: BTreeMap<String, Vec<String>>,
}

#[derive(Clone)]
pub struct Cursor {
    pub history: Arc<History>,
    pub offset: usize,
}

impl Repo {
    pub async fn history(&self, cancel: &CancellationToken) -> Result<Arc<History>> {
        let mut refs: BTreeMap<String, Vec<String>> = BTreeMap::new();
        let bytes = self.runner.run(&self.root, &args(&[
            "for-each-ref", "--format=%(objectname)%00%(objecttype)%00%(*objectname)%00%(*objecttype)%00%(refname)",
            "refs/heads", "refs/remotes", "refs/tags",
        ]), cancel).await?;
        for record in std::str::from_utf8(&bytes)?.lines() {
            let fields: Vec<_> = record.split('\0').collect();
            ensure!(fields.len() == 5, "Invalid ref record");
            let oid = if fields[1] == "commit" {
                Some(fields[0].to_owned())
            } else if fields[3] == "commit" {
                Some(fields[2].to_owned())
            } else if fields[1] == "tag" {
                // Nested annotated tags need recursive peeling; tags to blobs/trees are not history roots.
                let output = self
                    .runner
                    .output(
                        &self.root,
                        &args(&[
                            "rev-parse",
                            "--verify",
                            "--quiet",
                            &format!("{}^{{commit}}", fields[0]),
                        ]),
                        cancel,
                        1024,
                        false,
                        true,
                    )
                    .await?;
                if output.success {
                    Some(line(&output.bytes)?.into())
                } else {
                    None
                }
            } else {
                None
            };
            if let Some(oid) = oid {
                refs.entry(oid).or_default().push(fields[4].into());
            }
        }
        if let Some(head) = self.head(cancel).await? {
            refs.entry(head).or_default().push("HEAD".into());
        }
        let roots = refs.keys().cloned().collect();
        Ok(Arc::new(History {
            repo_id: self.info.repo_id.clone(),
            created: Instant::now(),
            roots,
            refs,
        }))
    }

    pub async fn log(
        &self,
        cursor: &Cursor,
        limit: usize,
        cancel: &CancellationToken,
    ) -> Result<(LogPage, bool)> {
        ensure!(
            (1..=200).contains(&limit),
            "Log page size must be between 1 and 200"
        );
        if cursor.history.roots.is_empty() {
            return Ok((
                LogPage {
                    commits: vec![],
                    next_cursor: None,
                },
                false,
            ));
        }
        let command = args(&[
            "log",
            "--stdin",
            "--topo-order",
            "--no-decorate",
            "--no-show-signature",
            "--no-notes",
            "--encoding=UTF-8",
            "-z",
            "--format=%H%x00%P%x00%an%x00%ae%x00%aI%x00%cI%x00%s",
            &format!("--max-count={}", limit + 1),
            &format!("--skip={}", cursor.offset),
        ]);
        // Keep the captured OIDs (not live ref names) without exceeding Windows'
        // command-line limit. The separator belongs to the stdin revision list.
        let revisions = format!("{}\n--\n", cursor.history.roots.join("\n"));
        let bytes = self
            .runner
            .run_with_input(&self.root, &command, revisions.as_bytes(), cancel)
            .await?;
        let text = std::str::from_utf8(&bytes).context("Commit metadata is not valid UTF-8")?;
        let fields: Vec<_> = text
            .strip_suffix('\0')
            .unwrap_or(text)
            .split('\0')
            .collect();
        if text.is_empty() {
            return Ok((
                LogPage {
                    commits: vec![],
                    next_cursor: None,
                },
                false,
            ));
        }
        ensure!(
            fields.len().is_multiple_of(7),
            "Invalid commit metadata framing"
        );
        let more = fields.len() / 7 > limit;
        let commits = fields
            .as_chunks::<7>()
            .0
            .iter()
            .take(limit)
            .map(|f| Commit {
                oid: f[0].into(),
                parents: f[1].split_whitespace().map(Into::into).collect(),
                author_name: f[2].into(),
                author_email: f[3].into(),
                author_date: f[4].into(),
                commit_date: f[5].into(),
                subject: f[6].into(),
                refs: cursor.history.refs.get(f[0]).cloned().unwrap_or_default(),
            })
            .collect();
        Ok((
            LogPage {
                commits,
                next_cursor: None,
            },
            more,
        ))
    }
}

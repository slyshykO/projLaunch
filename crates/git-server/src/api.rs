//! One method registry for stdio and the optional development HTTP/WS adapters.
use crate::{
    git::{
        self, Cursor,
        runner::{GitRunner, args},
    },
    session::Session,
};
use git_protocol::*;
use json_rpc::{Error, JsonRpc};
use std::sync::Arc;

#[derive(Default)]
pub struct GitService {
    pub runner: GitRunner,
}

fn rpc_error(error: anyhow::Error) -> Error {
    let message = format!("{error:#}");
    Error::rpc(
        if message == "Operation cancelled" {
            -32021
        } else {
            -32000
        },
        &message,
    )
}

pub fn git_api(session: Arc<Session>) -> JsonRpc {
    let hello = session.clone();
    let open = session.clone();
    let status = session.clone();
    let branches = session.clone();
    let log = session.clone();
    let files = session.clone();
    let patch = session.clone();
    JsonRpc::new()
        .add("server.hello", move |_: serde_json::Value| {
            let session = hello.clone();
            async move {
                let bytes = session
                    .service
                    .runner
                    .run(
                        std::path::Path::new("."),
                        &args(&["--version"]),
                        &session.closed,
                    )
                    .await
                    .map_err(rpc_error)?;
                Ok(Hello {
                    protocol_version: PROTOCOL_VERSION,
                    git_version: git::line(&bytes).map_err(rpc_error)?.into(),
                    capabilities: [
                        "repo.open",
                        "git.status",
                        "git.branches",
                        "git.log",
                        "git.diffFiles",
                        "git.diff",
                        "git.cancel",
                    ]
                    .into_iter()
                    .map(Into::into)
                    .collect(),
                })
            }
        })
        .add("repo.open", move |params: OpenParams| {
            let session = open.clone();
            async move {
                async {
                    let operation = session.operation(&params.operation_id)?;
                    let repo = git::open(
                        &params.path,
                        session.service.runner.clone(),
                        &operation.cancel,
                    )
                    .await?;
                    let mut repositories = session.repositories.lock().unwrap();
                    if let Some(existing) = repositories.values().find(|r| r.root == repo.root) {
                        return Ok(existing.info.clone());
                    }
                    anyhow::ensure!(
                        repositories.len() < 128,
                        "Too many open repositories; disconnect to release them"
                    );
                    repositories.insert(repo.info.repo_id.clone(), repo.clone());
                    Ok(repo.info.clone())
                }
                .await
                .map_err(rpc_error)
            }
        })
        .add("git.status", move |params: RepoParams| {
            let session = status.clone();
            async move {
                async {
                    let operation = session.operation(&params.operation_id)?;
                    session
                        .repo(&params.repo_id)?
                        .status(&operation.cancel)
                        .await
                }
                .await
                .map_err(rpc_error)
            }
        })
        .add("git.branches", move |params: RepoParams| {
            let session = branches.clone();
            async move {
                async {
                    let operation = session.operation(&params.operation_id)?;
                    session
                        .repo(&params.repo_id)?
                        .branches(&operation.cancel)
                        .await
                }
                .await
                .map_err(rpc_error)
            }
        })
        .add("git.log", move |params: LogParams| {
            let session = log.clone();
            async move {
                async {
                    let operation = session.operation(&params.operation_id)?;
                    let repo = session.repo(&params.repo_id)?;
                    let cursor = match params.cursor {
                        Some(id) => session.cursor(&id, &params.repo_id)?,
                        None => Cursor {
                            history: repo.history(&operation.cancel).await?,
                            offset: 0,
                        },
                    };
                    let (mut page, more) = repo
                        .log(&cursor, params.limit.unwrap_or(100), &operation.cancel)
                        .await?;
                    if more {
                        page.next_cursor = Some(session.save_cursor(Cursor {
                            history: cursor.history,
                            offset: cursor.offset + page.commits.len(),
                        }));
                    }
                    Ok(page)
                }
                .await
                .map_err(rpc_error)
            }
        })
        .add("git.diffFiles", move |params: DiffParams| {
            let session = files.clone();
            async move {
                async {
                    let operation = session.operation(&params.operation_id)?;
                    session
                        .repo(&params.repo_id)?
                        .diff_files(&params.target, &operation.cancel)
                        .await
                }
                .await
                .map_err(rpc_error)
            }
        })
        .add("git.diff", move |params: PatchParams| {
            let session = patch.clone();
            async move {
                async {
                    let operation = session.operation(&params.operation_id)?;
                    session
                        .repo(&params.repo_id)?
                        .patch(&params.target, &params.path, &operation.cancel)
                        .await
                }
                .await
                .map_err(rpc_error)
            }
        })
        .add("git.cancel", move |params: CancelParams| {
            let cancelled = session.cancel(&params.operation_id);
            async move { Ok(cancelled) }
        })
}

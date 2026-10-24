use crate::{
    endpoint::Endpoint,
    project::{ProjectData, Remote},
    project_git::{self, ProjectGitStatus, RepositoryStatus},
    storage::Storage,
};
use git_client::Client;
use git_protocol::*;
use std::{
    collections::HashMap,
    path::PathBuf,
    sync::{
        Arc, OnceLock,
        atomic::{AtomicU64, Ordering},
    },
};
use tauri::Emitter;
use tokio::sync::{Mutex, Semaphore};

type ConnectionSlot = Arc<Mutex<Option<Arc<Client>>>>;
type EventSink = Arc<dyn Fn(GitEvent) + Send + Sync>;

pub struct GitHost {
    connections: Mutex<HashMap<Endpoint, ConnectionSlot>>,
    project_checks: Semaphore,
    next_check: AtomicU64,
    events: OnceLock<EventSink>,
}

impl Default for GitHost {
    fn default() -> Self {
        Self {
            connections: Mutex::new(HashMap::new()),
            project_checks: Semaphore::new(4),
            next_check: AtomicU64::new(1),
            events: OnceLock::new(),
        }
    }
}

impl GitHost {
    pub fn set_app(&self, app: tauri::AppHandle) {
        // Keep the transport independent of a WebView runtime (also in native
        // tests). Only the application entry point installs the UI event sink.
        let _ = self.events.set(Arc::new(move |event| {
            if let Err(error) = app.emit_to("main", "git-repository-changed", event) {
                tracing::warn!(%error, "Could not forward Git notification");
            }
        }));
    }

    async fn client(&self, endpoint: &Endpoint) -> Result<Arc<Client>, String> {
        endpoint.validate().map_err(|e| e.to_string())?;
        let slot = self
            .connections
            .lock()
            .await
            .entry(endpoint.clone())
            .or_default()
            .clone();
        let mut connection = slot.lock().await;
        if let Some(client) = connection.as_ref().filter(|c| !c.is_closed()) {
            return Ok(client.clone());
        }
        // Repository handles belong to the old process. New operations must reopen them.
        if let Some(old) = connection.take() {
            old.shutdown().await;
        }
        let client = Arc::new(
            match endpoint {
                Endpoint::Local => Client::connect(&server_path()?).await,
                Endpoint::Wsl { distribution } => crate::wsl::connect(distribution).await,
                Endpoint::Ssh { host, username } => crate::ssh::connect(host, username).await,
            }
            .map_err(|e| format!("{e:#}"))?,
        );
        if let Some(emit) = self.events.get().cloned() {
            let endpoint = endpoint.clone();
            let mut events = client.events();
            tauri::async_runtime::spawn(async move {
                loop {
                    let (subscription_id, history, working_tree, error, closed) =
                        match events.recv().await {
                            Ok(git_client::Event::Changed(event)) => (
                                Some(event.subscription_id),
                                event.history,
                                event.working_tree,
                                event.error,
                                false,
                            ),
                            Ok(git_client::Event::Disconnected(error)) => {
                                (None, true, true, Some(error), true)
                            }
                            Err(tokio::sync::broadcast::error::RecvError::Lagged(_)) => (
                                None,
                                true,
                                true,
                                Some("Git notifications were missed; reconnecting".into()),
                                false,
                            ),
                            Err(tokio::sync::broadcast::error::RecvError::Closed) => break,
                        };
                    let event = GitEvent {
                        endpoint: endpoint.clone(),
                        subscription_id,
                        history,
                        working_tree,
                        error,
                    };
                    emit(event);
                    if closed {
                        break;
                    }
                }
            });
        }
        *connection = Some(client.clone());
        tracing::info!(?endpoint, "Git server connected");
        Ok(client)
    }

    pub async fn shutdown(&self) {
        let connections = std::mem::take(&mut *self.connections.lock().await);
        for slot in connections.into_values() {
            if let Some(client) = slot.lock().await.take() {
                client.shutdown().await;
            }
        }
    }

    async fn project_status(&self, project: ProjectData) -> Result<ProjectGitStatus, String> {
        let endpoint = match &project.remote {
            None => Endpoint::Local,
            Some(Remote::Wsl(distribution)) => Endpoint::Wsl {
                distribution: distribution.clone(),
            },
            Some(Remote::Ssh { host, username }) => Endpoint::Ssh {
                host: host.clone(),
                username: username.clone(),
            },
        };
        endpoint.validate().map_err(|e| e.to_string())?;
        let _permit = self
            .project_checks
            .acquire()
            .await
            .map_err(|e| e.to_string())?;
        let discovery = match &endpoint {
            Endpoint::Local => {
                tokio::task::spawn_blocking(move || project_git::discover(&project.path))
                    .await
                    .map_err(|e| e.to_string())?
                    .map_err(|e| format!("{e:#}"))?
            }
            Endpoint::Wsl { .. } | Endpoint::Ssh { .. } => self
                .client(&endpoint)
                .await?
                .call::<_, git_discovery::Discovery>(
                    "project.discover",
                    &OpenParams {
                        path: project.path,
                        operation_id: format!(
                            "discover-{}",
                            self.next_check.fetch_add(1, Ordering::Relaxed)
                        ),
                    },
                )
                .await
                .map_err(|e| format!("{e:#}"))?,
        };
        let mut result = ProjectGitStatus {
            warnings: discovery.warnings,
            ..Default::default()
        };
        if discovery.roots.is_empty() {
            return Ok(result);
        }
        // Keep open + status on the same connection; refresh reopens after a restart.
        let client = self.client(&endpoint).await;
        for root in discovery.roots {
            let mut entry = RepositoryStatus {
                endpoint: if endpoint == Endpoint::Local {
                    None
                } else {
                    Some(endpoint.clone())
                },
                name: root
                    .file_name()
                    .unwrap_or(root.as_os_str())
                    .to_string_lossy()
                    .into_owned(),
                root: root.to_string_lossy().into_owned(),
                branch: None,
                dirty: None,
                error: None,
            };
            match &client {
                Ok(client) => {
                    let id = self.next_check.fetch_add(1, Ordering::Relaxed);
                    let check = repository_status(client, &entry.root, id).await;
                    match check {
                        Ok((repository, status)) => {
                            entry.root = repository.root;
                            entry.branch = status.branch;
                            entry.dirty = Some(!status.entries.is_empty());
                        }
                        Err(error) => entry.error = Some(format!("{error:#}")),
                    }
                }
                Err(error) => entry.error = Some(error.clone()),
            }
            result.repositories.push(entry);
        }
        Ok(result)
    }
}

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct GitEvent {
    endpoint: Endpoint,
    subscription_id: Option<String>,
    history: bool,
    working_tree: bool,
    error: Option<String>,
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct WatchParams {
    path: String,
    subscription_id: String,
}

#[tauri::command]
pub async fn git_watch(
    host: tauri::State<'_, GitHost>,
    endpoint: Option<Endpoint>,
    params: WatchParams,
) -> Result<bool, String> {
    let client = host.client(&endpoint.unwrap_or_default()).await?;
    let repository: Repository = client
        .call(
            "repo.open",
            &OpenParams {
                path: params.path,
                operation_id: format!("watch-{}", params.subscription_id),
            },
        )
        .await
        .map_err(|e| format!("{e:#}"))?;
    client
        .call(
            "repo.subscribe",
            &SubscribeParams {
                repo_id: repository.repo_id,
                subscription_id: params.subscription_id,
            },
        )
        .await
        .map_err(|e| format!("{e:#}"))
}

#[tauri::command]
pub async fn git_unwatch(
    host: tauri::State<'_, GitHost>,
    endpoint: Option<Endpoint>,
    params: UnsubscribeParams,
) -> Result<bool, String> {
    // Disposing a view must never start/reconnect a server just to unsubscribe.
    let slot = host
        .connections
        .lock()
        .await
        .get(&endpoint.unwrap_or_default())
        .cloned();
    let client = match slot {
        Some(slot) => slot.lock().await.clone(),
        None => None,
    };
    match client.filter(|client| !client.is_closed()) {
        Some(client) => client
            .call("repo.unsubscribe", &params)
            .await
            .map_err(|e| format!("{e:#}")),
        None => Ok(false),
    }
}

async fn repository_status(
    client: &Client,
    root: &str,
    id: u64,
) -> anyhow::Result<(Repository, Status)> {
    let repository: Repository = client
        .call(
            "repo.open",
            &OpenParams {
                path: root.to_owned(),
                operation_id: format!("card-{id}-open"),
            },
        )
        .await?;
    let status = client
        .call(
            "git.status",
            &RepoParams {
                repo_id: repository.repo_id.clone(),
                operation_id: format!("card-{id}-status"),
            },
        )
        .await?;
    Ok((repository, status))
}

fn server_path() -> Result<PathBuf, String> {
    let executable = std::env::current_exe()
        .map_err(|e| format!("Cannot determine the launcher executable path: {e}"))?;
    find_server_path(
        &executable,
        std::path::Path::new(env!("CARGO_MANIFEST_DIR")),
    )
}

fn find_server_path(
    executable: &std::path::Path,
    development_root: &std::path::Path,
) -> Result<PathBuf, String> {
    let filename = concat!(
        "project-launch-git-server-",
        env!("PROJLAUNCH_TARGET"),
        env!("PROJLAUNCH_EXE_SUFFIX")
    );
    let packaged = executable.with_file_name("binaries").join(filename);
    let adjacent = executable.with_file_name(if cfg!(windows) {
        "project-launch-git-server.exe"
    } else {
        "project-launch-git-server"
    });
    // Tauri's beforeDevCommand/beforeBuildCommand prepares this target-specific sidecar.
    let development = development_root.join("binaries").join(filename);
    let mut errors = Vec::new();
    for path in [packaged, adjacent, development] {
        match path.metadata() {
            Ok(metadata) if metadata.is_file() => return Ok(path),
            Ok(_) => errors.push(format!(
                "Git server path is not a file: '{}'",
                path.display()
            )),
            Err(error) => errors.push(format!("Cannot inspect '{}': {error}", path.display())),
        }
    }
    Err(format!(
        "Git server is unavailable. {}. Build it with npm run build:git-server.",
        errors.join("; ")
    ))
}

macro_rules! command {
    ($name:ident, $params:ty, $result:ty, $method:literal) => {
        #[tauri::command]
        pub async fn $name(
            host: tauri::State<'_, GitHost>,
            params: $params,
            endpoint: Option<Endpoint>,
        ) -> Result<$result, String> {
            host.client(&endpoint.unwrap_or_default())
                .await?
                .call($method, &params)
                .await
                .map_err(|e| format!("{e:#}"))
        }
    };
}

#[tauri::command]
pub async fn git_hello(
    host: tauri::State<'_, GitHost>,
    endpoint: Option<Endpoint>,
) -> Result<Hello, String> {
    host.client(&endpoint.unwrap_or_default())
        .await?
        .call("server.hello", &serde_json::json!({}))
        .await
        .map_err(|e| format!("{e:#}"))
}
command!(git_open, OpenParams, Repository, "repo.open");
command!(git_status, RepoParams, Status, "git.status");

#[tauri::command]
pub async fn git_project_status(
    host: tauri::State<'_, GitHost>,
    storage: tauri::State<'_, Storage>,
    id: String,
) -> Result<ProjectGitStatus, String> {
    host.project_status(storage.get(id).await?).await
}
command!(git_log, LogParams, LogPage, "git.log");
command!(git_branches, RepoParams, BranchList, "git.branches");
command!(git_diff_files, DiffParams, Vec<DiffFile>, "git.diffFiles");
command!(git_diff, PatchParams, Patch, "git.diff");
command!(git_cancel, CancelParams, bool, "git.cancel");

#[tauri::command]
pub async fn git_disconnect(host: tauri::State<'_, GitHost>) -> Result<(), String> {
    host.shutdown().await;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::{fs, path::Path, process::Command};

    #[tokio::test]
    async fn portable_layout_finds_and_starts_packaged_server_without_checkout() {
        let directory = tempfile::tempdir().unwrap();
        let root = directory.path().join("Extracted With Spaces");
        let binaries = root.join("binaries");
        fs::create_dir_all(&binaries).unwrap();
        let filename = concat!(
            "project-launch-git-server-",
            env!("PROJLAUNCH_TARGET"),
            env!("PROJLAUNCH_EXE_SUFFIX")
        );
        let packaged = binaries.join(filename);
        fs::copy(server_path().unwrap(), &packaged).unwrap();
        let executable = root.join("projLaunch.exe");
        // An unrelated/old adjacent sidecar must not override the packaged one.
        fs::write(
            root.join(if cfg!(windows) {
                "project-launch-git-server.exe"
            } else {
                "project-launch-git-server"
            }),
            "stale",
        )
        .unwrap();
        let found =
            find_server_path(&executable, &directory.path().join("missing checkout")).unwrap();
        assert_eq!(found, packaged);
        let client = Client::connect(&found).await.unwrap();
        let hello: Hello = client
            .call("server.hello", &serde_json::json!({}))
            .await
            .unwrap();
        assert_eq!(hello.protocol_version, PROTOCOL_VERSION);
        client.shutdown().await;
    }

    #[test]
    fn server_lookup_preserves_legacy_and_development_paths_and_reports_failures() {
        let directory = tempfile::tempdir().unwrap();
        let root = directory.path();
        let executable = root.join("projLaunch.exe");
        let development = root.join("checkout");
        let filename = concat!(
            "project-launch-git-server-",
            env!("PROJLAUNCH_TARGET"),
            env!("PROJLAUNCH_EXE_SUFFIX")
        );
        let compiled = development.join("binaries").join(filename);
        fs::create_dir_all(compiled.parent().unwrap()).unwrap();
        fs::write(&compiled, "development").unwrap();
        assert_eq!(
            find_server_path(&executable, &development).unwrap(),
            compiled
        );
        let adjacent = root.join(if cfg!(windows) {
            "project-launch-git-server.exe"
        } else {
            "project-launch-git-server"
        });
        fs::write(&adjacent, "legacy").unwrap();
        assert_eq!(
            find_server_path(&executable, &development).unwrap(),
            adjacent
        );
        fs::remove_file(&adjacent).unwrap();
        fs::remove_file(&compiled).unwrap();
        let error = find_server_path(&executable, &development).unwrap_err();
        assert!(error.contains(&root.join("binaries").join(filename).display().to_string()));
        assert!(error.contains(&adjacent.display().to_string()));
        assert!(error.contains(&compiled.display().to_string()));
    }

    fn git(root: &Path, args: &[&str]) {
        let mut command = Command::new("git");
        command.current_dir(root).args(args);
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            command.creation_flags(0x08000000);
        }
        let output = command.output().unwrap();
        assert!(
            output.status.success(),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
    }

    fn project(path: &Path) -> ProjectData {
        ProjectData {
            id: "fixture".into(),
            name: "Fixture".into(),
            path: path.to_str().unwrap().into(),
            description: String::new(),
            ide: "vscode".into(),
            last_opened: "2026-10-19T00:00:00Z".into(),
            environment: Default::default(),
            remote: None,
        }
    }

    #[tokio::test]
    async fn subscription_events_keep_endpoint_identity_and_report_disconnect() {
        let temp = tempfile::tempdir().unwrap();
        git(temp.path(), &["init", "--quiet"]);
        let host = GitHost::default();
        let (tx, mut events) = tokio::sync::mpsc::unbounded_channel();
        assert!(
            host.events
                .set(Arc::new(move |event| {
                    let _ = tx.send(event);
                }))
                .is_ok()
        );
        let client = host.client(&Endpoint::Local).await.unwrap();
        let (repository, _) = repository_status(&client, temp.path().to_str().unwrap(), 1)
            .await
            .unwrap();
        client
            .call::<_, bool>(
                "repo.subscribe",
                &SubscribeParams {
                    repo_id: repository.repo_id,
                    subscription_id: "host-watch".into(),
                },
            )
            .await
            .unwrap();
        fs::write(temp.path().join("new.txt"), "observed").unwrap();
        let event = tokio::time::timeout(std::time::Duration::from_secs(8), events.recv())
            .await
            .unwrap()
            .unwrap();
        assert_eq!(event.endpoint, Endpoint::Local);
        assert_eq!(event.subscription_id.as_deref(), Some("host-watch"));
        assert!(event.working_tree && !event.history && event.error.is_none());
        host.shutdown().await;
        tokio::time::timeout(std::time::Duration::from_secs(8), async {
            loop {
                let event = events.recv().await.unwrap();
                if event.subscription_id.is_none() {
                    assert_eq!(event.endpoint, Endpoint::Local);
                    assert!(event.error.is_some());
                    break;
                }
            }
        })
        .await
        .unwrap();
    }

    #[tokio::test]
    async fn cards_read_real_status_and_reopen_after_server_restart() {
        let temp = tempfile::tempdir().unwrap();
        let first = temp.path().join("first");
        let second = temp.path().join("second");
        for root in [&first, &second] {
            fs::create_dir(root).unwrap();
            git(root, &["init", "--quiet"]);
        }
        fs::write(first.join("new.txt"), "untracked").unwrap();
        let workspace = temp.path().join("fixture.code-workspace");
        fs::write(
            &workspace,
            r#"{"folders":[{"path":"first"},{"path":"second"},{"path":"first"}]}"#,
        )
        .unwrap();
        let host = GitHost::default();
        let status = host.project_status(project(&workspace)).await.unwrap();
        assert_eq!(status.repositories.len(), 2);
        assert!(
            status.repositories.iter().all(|repo| repo.error.is_none()),
            "{status:?}"
        );
        assert_eq!(
            status
                .repositories
                .iter()
                .find(|repo| repo.name == "first")
                .unwrap()
                .dirty,
            Some(true)
        );
        assert_eq!(
            status
                .repositories
                .iter()
                .find(|repo| repo.name == "second")
                .unwrap()
                .dirty,
            Some(false)
        );
        git(&first, &["add", "new.txt"]);
        assert_eq!(
            host.project_status(project(&first))
                .await
                .unwrap()
                .repositories[0]
                .dirty,
            Some(true)
        );
        git(
            &first,
            &[
                "-c",
                "user.name=Fixture",
                "-c",
                "user.email=fixture@example.test",
                "commit",
                "-qm",
                "Initial",
            ],
        );
        assert_eq!(
            host.project_status(project(&first))
                .await
                .unwrap()
                .repositories[0]
                .dirty,
            Some(false)
        );
        fs::write(first.join("new.txt"), "modified").unwrap();
        host.shutdown().await;
        assert_eq!(
            host.project_status(project(&first))
                .await
                .unwrap()
                .repositories[0]
                .dirty,
            Some(true)
        );
        host.shutdown().await;
    }

    #[tokio::test]
    async fn invalid_remote_and_non_repository_projects_do_not_start_server_and_invalid_git_is_unknown()
     {
        let temp = tempfile::tempdir().unwrap();
        let host = GitHost::default();
        let mut remote = project(temp.path());
        remote.remote = Some(crate::project::Remote::Ssh {
            host: "-oProxyCommand=invalid".into(),
            username: "user".into(),
        });
        assert!(host.project_status(remote).await.is_err());
        assert!(
            host.project_status(project(temp.path()))
                .await
                .unwrap()
                .repositories
                .is_empty()
        );
        assert!(host.connections.lock().await.is_empty());
        fs::create_dir(temp.path().join(".git")).unwrap();
        let status = host.project_status(project(temp.path())).await.unwrap();
        assert_eq!(status.repositories[0].dirty, None);
        assert!(status.repositories[0].error.is_some());
        host.shutdown().await;
    }
}

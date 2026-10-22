use crate::{
    project::ProjectData,
    project_git::{self, ProjectGitStatus, RepositoryStatus},
    storage::Storage,
};
use git_client::Client;
use git_protocol::*;
use std::{
    path::PathBuf,
    sync::{
        Arc,
        atomic::{AtomicU64, Ordering},
    },
};
use tokio::sync::{Mutex, Semaphore};

pub struct GitHost {
    connection: Mutex<Option<Arc<Client>>>,
    project_checks: Semaphore,
    next_check: AtomicU64,
}

impl Default for GitHost {
    fn default() -> Self {
        Self {
            connection: Mutex::new(None),
            project_checks: Semaphore::new(4),
            next_check: AtomicU64::new(1),
        }
    }
}

impl GitHost {
    async fn client(&self) -> Result<Arc<Client>, String> {
        let mut connection = self.connection.lock().await;
        if let Some(client) = connection.as_ref().filter(|c| !c.is_closed()) {
            return Ok(client.clone());
        }
        // Repository handles belong to the old process. New operations must reopen them.
        if let Some(old) = connection.take() {
            old.shutdown().await;
        }
        let executable = server_path()?;
        tracing::info!("Starting local Git server");
        let client = Arc::new(
            Client::connect(&executable)
                .await
                .map_err(|e| format!("{e:#}"))?,
        );
        *connection = Some(client.clone());
        tracing::info!("Local Git server connected");
        Ok(client)
    }

    pub async fn shutdown(&self) {
        if let Some(client) = self.connection.lock().await.take() {
            tracing::debug!("Stopping local Git server");
            client.shutdown().await;
        }
    }

    async fn project_status(&self, project: ProjectData) -> Result<ProjectGitStatus, String> {
        if project.remote.is_some() {
            return Ok(ProjectGitStatus {
                unsupported: true,
                ..Default::default()
            });
        }
        let _permit = self
            .project_checks
            .acquire()
            .await
            .map_err(|e| e.to_string())?;
        let discovery = tokio::task::spawn_blocking(move || project_git::discover(&project.path))
            .await
            .map_err(|e| e.to_string())?
            .map_err(|e| format!("{e:#}"))?;
        let mut result = ProjectGitStatus {
            warnings: discovery.warnings,
            ..Default::default()
        };
        if discovery.roots.is_empty() {
            return Ok(result);
        }
        // Keep open + status on the same connection; refresh reopens after a restart.
        let client = self.client().await;
        for root in discovery.roots {
            let mut entry = RepositoryStatus {
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
    let adjacent = executable.with_file_name(if cfg!(windows) {
        "project-launch-git-server.exe"
    } else {
        "project-launch-git-server"
    });
    // Tauri's beforeDevCommand/beforeBuildCommand prepares this target-specific sidecar.
    let development = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("binaries")
        .join(concat!(
            "project-launch-git-server-",
            env!("PROJLAUNCH_TARGET"),
            env!("PROJLAUNCH_EXE_SUFFIX")
        ));
    let mut errors = Vec::new();
    for path in [adjacent, development] {
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
        ) -> Result<$result, String> {
            host.client()
                .await?
                .call($method, &params)
                .await
                .map_err(|e| format!("{e:#}"))
        }
    };
}

#[tauri::command]
pub async fn git_hello(host: tauri::State<'_, GitHost>) -> Result<Hello, String> {
    host.client()
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
    async fn remote_and_non_repository_projects_do_not_start_server_and_invalid_git_is_unknown() {
        let temp = tempfile::tempdir().unwrap();
        let host = GitHost::default();
        let mut remote = project(temp.path());
        remote.remote = Some(crate::project::Remote::Wsl("Ubuntu".into()));
        assert!(host.project_status(remote).await.unwrap().unsupported);
        assert!(
            host.project_status(project(temp.path()))
                .await
                .unwrap()
                .repositories
                .is_empty()
        );
        assert!(host.connection.lock().await.is_none());
        fs::create_dir(temp.path().join(".git")).unwrap();
        let status = host.project_status(project(temp.path())).await.unwrap();
        assert_eq!(status.repositories[0].dirty, None);
        assert!(status.repositories[0].error.is_some());
        host.shutdown().await;
    }
}

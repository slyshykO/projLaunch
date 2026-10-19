use git_client::Client;
use git_protocol::*;
use std::{path::PathBuf, sync::Arc};
use tokio::sync::Mutex;

#[derive(Default)]
pub struct GitHost {
    connection: Mutex<Option<Arc<Client>>>,
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
        let client = Arc::new(
            Client::connect(&executable)
                .await
                .map_err(|e| format!("{e:#}"))?,
        );
        *connection = Some(client.clone());
        Ok(client)
    }

    pub async fn shutdown(&self) {
        if let Some(client) = self.connection.lock().await.take() {
            client.shutdown().await;
        }
    }
}

fn server_path() -> Result<PathBuf, String> {
    let executable = std::env::current_exe().map_err(|e| e.to_string())?;
    let adjacent = executable.with_file_name(if cfg!(windows) {
        "git-server.exe"
    } else {
        "git-server"
    });
    if adjacent.is_file() {
        return Ok(adjacent);
    }
    // Tauri's beforeDevCommand/beforeBuildCommand prepares this target-specific sidecar.
    let development = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("binaries")
        .join(concat!(
            "git-server-",
            env!("PROJLAUNCH_TARGET"),
            env!("PROJLAUNCH_EXE_SUFFIX")
        ));
    if development.is_file() {
        return Ok(development);
    }
    Err("Git server is missing. Build it with npm run build:git-server.".into())
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
command!(git_log, LogParams, LogPage, "git.log");
command!(git_diff_files, DiffParams, Vec<DiffFile>, "git.diffFiles");
command!(git_diff, PatchParams, Patch, "git.diff");
command!(git_cancel, CancelParams, bool, "git.cancel");

#[tauri::command]
pub async fn git_disconnect(host: tauri::State<'_, GitHost>) -> Result<(), String> {
    host.shutdown().await;
    Ok(())
}

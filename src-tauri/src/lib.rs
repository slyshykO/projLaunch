mod git_host;
mod project;
mod storage;

use project::Remote;
use storage::Storage;
use tauri::Manager;

#[tauri::command]
fn greet(name: &str) -> String {
    format!("Hello, {}! You've been greeted from Rust!", name)
}

#[tauri::command]
fn get_data_dir(app_handle: tauri::AppHandle) -> Result<String, tauri::Error> {
    Ok(app_handle
        .path()
        .app_data_dir()?
        .to_string_lossy()
        .into_owned())
}

#[tauri::command]
async fn get_projects(storage: tauri::State<'_, Storage>) -> Result<Vec<String>, String> {
    storage.list().await
}

fn ide_to_command(ide: &str) -> String {
    let ide = ide.to_lowercase();
    let code_exe = if cfg!(windows) {
        "code.cmd".to_string()
    } else {
        "code".to_string()
    };
    match ide.as_str() {
        "code" => code_exe,
        "vscode" => code_exe,
        "idea" => "idea".to_string(),
        "rider" => "rider".to_string(),
        s if s.contains("studio") && s.contains("2022") => {
            r"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe"
                .to_string()
        }
        s if s.contains("studio") && s.contains("2019") => {
            r"C:\Program Files\Microsoft Visual Studio\2019\Community\Common7\IDE\devenv.exe"
                .to_string()
        }
        s if s.contains("studio") && s.contains("2026") => {
            r"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe"
                .to_string()
        }
        _ => code_exe,
    }
}

#[tauri::command]
async fn open_project(storage: tauri::State<'_, Storage>, id: String) -> Result<(), String> {
    let project_data = storage.get(id).await?;
    let ide = project_data.ide;
    let path = project_data.path;
    let environment = project_data.environment;
    let app = ide_to_command(ide.as_str());
    let mut cmd = std::process::Command::new(app.as_str());
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;

        // Keep console-based IDE launchers (such as code.cmd) from opening a console window.
        const CREATE_NO_WINDOW: u32 = 0x08000000;
        cmd.creation_flags(CREATE_NO_WINDOW);
    }
    match project_data.remote {
        Some(Remote::Ssh { host, username }) => {
            let host = if username.is_empty() {
                host
            } else {
                format!("{}@{}", username, host)
            };
            cmd.args(["--remote", format!("ssh-remote+{}", host).as_str()]);
        }
        Some(Remote::Wsl(distro)) => {
            cmd.args(["--remote", format!("wsl+{}", distro).as_str()]);
        }
        None => {}
    }
    cmd.args([path.as_str()]);
    for (key, value) in environment {
        if key == "PATH" {
            let path = std::env::var("PATH").unwrap_or_default();
            let path_delimiter = if cfg!(windows) { ";" } else { ":" };
            let value = if path.is_empty() {
                value
            } else {
                format!("{}{}{}", value, path_delimiter, path)
            };
            cmd.env(key, value);
        } else {
            cmd.env(key, value);
        }
    }
    cmd.spawn()
        .map_err(|e| format!("can't run {}. {}", app, e))?;
    Ok(())
}

// Keep the existing command name for IPC compatibility; all writes now use SQLite.
#[tauri::command]
async fn rewrite_project_file(
    storage: tauri::State<'_, Storage>,
    id: String,
    content: String,
) -> Result<(), String> {
    storage.save(id, content).await
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_window_state::Builder::new().build())
        .plugin(tauri_plugin_opener::init())
        .manage(git_host::GitHost::default())
        .setup(|app| {
            let directory = app.path().app_data_dir().map_err(|error| error.to_string());
            app.manage(Storage::start(directory));
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            greet,
            get_data_dir,
            get_projects,
            open_project,
            rewrite_project_file,
            git_host::git_hello,
            git_host::git_open,
            git_host::git_status,
            git_host::git_log,
            git_host::git_diff_files,
            git_host::git_diff,
            git_host::git_cancel,
            git_host::git_disconnect
        ])
        .build(tauri::generate_context!())
        .expect("error while building tauri application")
        .run(|app, event| {
            if matches!(event, tauri::RunEvent::Exit) {
                tauri::async_runtime::block_on(app.state::<git_host::GitHost>().shutdown());
            }
        });
}

mod git_host;
mod logging;
mod project;
mod project_git;
mod storage;
mod workspace;

use project::Remote;
use storage::Storage;
use tauri::{
    Emitter, Manager,
    menu::{MenuBuilder, MenuItemBuilder, SubmenuBuilder},
};

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

#[tauri::command]
async fn get_storage_access(storage: tauri::State<'_, Storage>) -> Result<storage::Access, String> {
    storage.access().await
}

#[tauri::command]
async fn load_workspace(
    storage: tauri::State<'_, Storage>,
) -> Result<Option<workspace::Workspace>, String> {
    storage.load_workspace().await
}

#[tauri::command]
async fn save_workspace(
    storage: tauri::State<'_, Storage>,
    workspace: workspace::Workspace,
) -> Result<(), String> {
    storage.save_workspace(workspace).await
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
            app.manage(logging::Logging::start(
                directory.as_deref().map_err(String::as_str),
            ));
            tracing::info!(version = env!("CARGO_PKG_VERSION"), "ProjLaunch starting");
            let add_project = MenuItemBuilder::with_id("add-project", "Add project…")
                .accelerator("CmdOrCtrl+N")
                .build(app)?;
            let close_tab =
                MenuItemBuilder::with_id("close-tab", "Close repository tab").build(app)?;
            let exit = MenuItemBuilder::with_id("exit", "Exit").build(app)?;
            let file_menu = SubmenuBuilder::new(app, "File")
                .item(&add_project)
                .item(&close_tab)
                .separator()
                .item(&exit)
                .build()?;
            let projects = MenuItemBuilder::with_id("projects", "Projects").build(app)?;
            let refresh = MenuItemBuilder::with_id("refresh", "Refresh Git status").build(app)?;
            let view_menu = SubmenuBuilder::new(app, "View")
                .item(&projects)
                .item(&refresh)
                .build()?;
            let about = MenuItemBuilder::with_id("about", "About ProjLaunch…").build(app)?;
            let help_menu = SubmenuBuilder::new(app, "Help").item(&about).build()?;
            let menu = MenuBuilder::new(app);
            #[cfg(target_os = "macos")]
            let menu = menu.item(
                &SubmenuBuilder::new(app, "Project Launcher")
                    .quit()
                    .build()?,
            );
            let menu = menu.item(&file_menu).item(&view_menu).item(&help_menu);
            #[cfg(target_os = "macos")]
            let menu = menu.item(
                &SubmenuBuilder::new(app, "Edit")
                    .undo()
                    .redo()
                    .separator()
                    .cut()
                    .copy()
                    .paste()
                    .select_all()
                    .build()?,
            );
            app.set_menu(menu.build()?)?;
            app.on_menu_event(|app, event| {
                if event.id() == "add-project" {
                    if let Err(error) = app.emit_to("main", "menu-add-project", ()) {
                        tracing::error!(%error, "Could not open project editor");
                    }
                } else if event.id() == "exit" {
                    if let Some(window) = app.get_webview_window("main") {
                        // Use the normal close flow, including saving window state.
                        if let Err(error) = window.close() {
                            tracing::error!(%error, "Could not close the main window");
                        }
                    }
                } else if let Err(error) = app.emit_to("main", "menu-action", event.id().as_ref()) {
                    tracing::error!(%error, "Could not dispatch menu action");
                }
            });
            app.manage(Storage::start(directory));
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_data_dir,
            get_projects,
            get_storage_access,
            load_workspace,
            save_workspace,
            open_project,
            rewrite_project_file,
            logging::frontend_log,
            git_host::git_hello,
            git_host::git_open,
            git_host::git_status,
            git_host::git_project_status,
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
                if let Err(error) =
                    tauri::async_runtime::block_on(app.state::<Storage>().shutdown())
                {
                    tracing::error!(%error, "Could not shut down database storage cleanly");
                }
                tauri::async_runtime::block_on(app.state::<git_host::GitHost>().shutdown());
                tracing::info!("ProjLaunch exiting");
                app.state::<logging::Logging>().shutdown();
            }
        });
}

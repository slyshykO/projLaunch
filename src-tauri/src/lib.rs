mod git_host;
mod project;
mod project_git;
mod storage;

use project::Remote;
use storage::Storage;
use tauri::{
    Emitter, Manager,
    menu::{MenuBuilder, MenuItemBuilder, SubmenuBuilder},
};
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};

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
    init_tracing();
    tauri::Builder::default()
        .plugin(tauri_plugin_window_state::Builder::new().build())
        .plugin(tauri_plugin_opener::init())
        .manage(git_host::GitHost::default())
        .setup(|app| {
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
                        eprintln!("Could not open project editor: {error}");
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
            let directory = app.path().app_data_dir().map_err(|error| error.to_string());
            app.manage(Storage::start(directory));
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_data_dir,
            get_projects,
            open_project,
            rewrite_project_file,
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
                tauri::async_runtime::block_on(app.state::<git_host::GitHost>().shutdown());
            }
        });
}

fn init_tracing() {
    use tracing_subscriber::EnvFilter;

    let env_var = std::env::var("PROJ_LAUNCHER_LOG").unwrap_or_else(|_| "info".into());

    let filter = EnvFilter::new(env_var).add_directive("tauri=info".parse().unwrap());

    tracing_subscriber::registry()
        .with(filter)
        .with(tracing_subscriber::fmt::layer())
        .init();
}

use fs_err::{File, OpenOptions};
use std::io::{Read, Write};

use tauri::Manager;

#[derive(Debug, Clone, serde::Deserialize, serde::Serialize)]
struct ProjectData {
    id: String,
    name: String,
    #[serde(rename = "lastOpened")]
    last_opened: String,
    description: String,
    path: String,
    ide: String,
    environment: std::collections::HashMap<String, String>,
}

fn file_content<P>(file_name: P) -> std::io::Result<String>
where
    P: Into<std::path::PathBuf>,
{
    let mut s = String::new();
    File::open(file_name)
        .and_then(|mut f| f.read_to_string(&mut s))
        .map(|_| s.trim().to_owned())
}

pub fn rewrite_file_content<P, C>(file_name: P, new_content: C) -> std::io::Result<()>
where
    //    P: AsRef<std::path::Path>,
    P: Into<std::path::PathBuf>,
    C: AsRef<str>,
{
    OpenOptions::new()
        .write(true)
        .create(true)
        .truncate(true)
        .open(file_name)
        .and_then(|mut f| f.write_all(new_content.as_ref().as_bytes()))?;
    Ok(())
}

// Learn more about Tauri commands at https://tauri.app/develop/calling-rust/
#[tauri::command]
fn greet(name: &str) -> String {
    format!("Hello, {}! You've been greeted from Rust!", name)
}

#[tauri::command]
fn get_data_dir(app_handle: tauri::AppHandle) -> Result<String, tauri::Error> {
    let dir = app_handle
        .path()
        .app_data_dir()?
        .to_string_lossy()
        .to_string();
    Ok(dir)
}

#[tauri::command]
fn get_projects(app_handle: tauri::AppHandle) -> Result<Vec<String>, tauri::Error> {
    let dir = app_handle.path().app_data_dir()?;
    let projects_dir = dir.join("projects");
    let projects: Vec<String> = std::fs::read_dir(&projects_dir)?
        .filter_map(|entry| {
            let entry = entry.ok()?;
            let path = entry.path();
            let file = file_content(path).ok()?;
            Some(file)
        })
        .collect();
    Ok(projects)
}

fn get_project_data<P>(path: P, id: String) -> Result<ProjectData, tauri::Error>
where
    P: Into<std::path::PathBuf>,
{
    let projects: Vec<ProjectData> = fs_err::read_dir(path)?
        .filter_map(|entry| {
            let entry = entry.ok()?;
            let path = entry.path();
            let file = file_content(path).ok()?;
            let project_data: ProjectData = serde_json::from_str(&file).ok()?;
            Some(project_data)
        })
        .collect();
    let project = projects.iter().find(|project| project.id == id);
    match project {
        Some(project) => Ok(project.clone()),
        None => Err(tauri::Error::from(anyhow::anyhow!(
            "Project `{}` not found",
            id
        ))),
    }
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
        s if s.contains("studio") && s.contains("2022") => {
            r"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe"
                .to_string()
        }
        _ => code_exe,
    }
}

#[tauri::command]
fn open_project(app_handle: tauri::AppHandle, id: String) -> Result<(), tauri::Error> {
    let dir = app_handle.path().app_data_dir()?;
    let projects_dir = dir.join("projects");
    let project_data: ProjectData = get_project_data(projects_dir, id.clone())?;
    let ide = project_data.ide;
    let path = project_data.path;
    let environment = project_data.environment;
    let app = ide_to_command(ide.as_str());
    let mut cmd = std::process::Command::new(app.as_str());
    //let mut cmd = std::process::Command::new("code.cmd");
    cmd.args(&[path.as_str()]);
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
        .map_err(|e| anyhow::anyhow!("can't run {}. {}", app, e))?;
    Ok(())
}

#[tauri::command]
async fn rewrite_project_file(
    app_handle: tauri::AppHandle,
    id: String,
    content: String,
) -> Result<(), tauri::Error> {
    let dir = app_handle.path().app_data_dir()?;
    let projects_dir = dir.join("projects");
    // get filename of project with id
    let project_file: Vec<_> = fs_err::read_dir(&projects_dir)?
        .filter_map(|entry| {
            let entry = entry.ok()?;
            let path = entry.path();
            let file = file_content(&path).ok()?;
            let project_data: ProjectData = serde_json::from_str(&file).ok()?;
            if project_data.id == id {
                Some(path)
            } else {
                None
            }
        })
        .collect();
    if project_file.len() == 0 {
        let new_file = projects_dir.join(id);
        rewrite_file_content(new_file, content)?; // create new file
    } else {
        let project_file = project_file[0].clone();
        rewrite_file_content(project_file, content)?;
    }

    Ok(())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            greet,
            get_data_dir,
            get_projects,
            open_project,
            rewrite_project_file
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

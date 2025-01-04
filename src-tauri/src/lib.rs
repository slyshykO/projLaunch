use tauri::Manager;

#[derive(Debug, serde::Deserialize, serde::Serialize)]
struct ProjectData {
    name: String,
    #[serde(rename = "lastOpened")]
    last_opened: time::OffsetDateTime,
    description: String,
    path: String,
    ide: String,
    environment: std::collections::HashMap<String, String>,
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

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![greet, get_data_dir])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

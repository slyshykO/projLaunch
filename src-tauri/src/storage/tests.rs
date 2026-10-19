use std::{fs, path::Path, sync::Arc};

use rusqlite::Connection;
use serde_json::{Value, json};
use tempfile::TempDir;

use super::{Storage, database::Database, legacy_import::IMPORT_NAME, migrations};
use crate::project::{ProjectData, Remote};

fn project(id: &str) -> Value {
    json!({
        "id": id,
        "name": "Проєкт 'one'",
        "lastOpened": "2026-10-19T11:22:33.123+03:00",
        "description": "Description\nwith a new line",
        "path": "C:\\work space\\workspace.code-workspace",
        "ide": "vscode",
        "environment": {"PATH": "C:\\tools;D:\\bin", "CUSTOM": "значення"},
        "remote": null
    })
}

fn legacy(dir: &Path, filename: &str, value: &Value) -> Vec<u8> {
    fs::create_dir_all(dir.join("projects")).unwrap();
    let bytes = serde_json::to_vec_pretty(value).unwrap();
    fs::write(dir.join("projects").join(filename), &bytes).unwrap();
    bytes
}

fn count(connection: &Connection, table: &str) -> i64 {
    connection
        .query_row(&format!("SELECT count(*) FROM {table}"), [], |row| {
            row.get(0)
        })
        .unwrap()
}

fn raw_database(dir: &Path) -> Connection {
    Connection::open(dir.join("projlaunch.db")).unwrap()
}

#[test]
fn imports_all_fields_and_keeps_source_bytes() {
    let dir = TempDir::new().unwrap();
    let local = project("local");
    let mut ssh = project("ssh");
    ssh["remote"] = json!({"Ssh": {"host": "host.example", "username": "alex"}});
    let mut wsl = project("wsl");
    wsl["remote"] = json!({"Wsl": "Ubuntu"});
    let sources = [
        ("project-local.json", local),
        ("ssh", ssh),
        ("wsl.custom", wsl),
    ];
    let originals: Vec<_> = sources
        .iter()
        .map(|(name, value)| legacy(dir.path(), name, value))
        .collect();

    let db = Database::open(dir.path()).unwrap();
    assert_eq!(db.list().unwrap().len(), 3);
    for ((name, value), bytes) in sources.iter().zip(originals) {
        let id = value["id"].as_str().unwrap();
        assert_eq!(serde_json::to_value(db.get(id).unwrap()).unwrap(), *value);
        assert_eq!(
            fs::read(dir.path().join("projects").join(name)).unwrap(),
            bytes
        );
    }
    let imported: i64 = db
        .connection
        .query_row(
            "SELECT item_count FROM data_migrations WHERE name = ?1",
            [IMPORT_NAME],
            |row| row.get(0),
        )
        .unwrap();
    assert_eq!(imported, 3);
}

#[test]
fn legacy_remote_arrays_missing_remote_and_bom_are_supported() {
    let dir = TempDir::new().unwrap();
    let mut ssh = project("ssh");
    ssh["remote"] = json!(["Ssh", "host", ""]);
    let mut wsl = project("wsl");
    wsl["remote"] = json!(["Wsl", "Ubuntu-24.04"]);
    let mut local = project("local");
    local.as_object_mut().unwrap().remove("remote");
    // Preserve older date strings as-is instead of imposing a new encoding.
    local["lastOpened"] = json!("2021-09-01T00:00:00");
    legacy(dir.path(), "ssh", &ssh);
    legacy(dir.path(), "wsl", &wsl);
    legacy(dir.path(), "local", &local);
    fs::write(
        dir.path().join("projects/local"),
        format!("\u{feff}{local}"),
    )
    .unwrap();
    let db = Database::open(dir.path()).unwrap();
    assert_eq!(
        db.get("ssh").unwrap().remote,
        Some(Remote::Ssh {
            host: "host".into(),
            username: "".into()
        })
    );
    assert_eq!(
        db.get("wsl").unwrap().remote,
        Some(Remote::Wsl("Ubuntu-24.04".into()))
    );
    assert_eq!(db.get("local").unwrap().remote, None);
    assert_eq!(db.get("local").unwrap().last_opened, "2021-09-01T00:00:00");
}

#[test]
fn fresh_install_creates_empty_database_and_does_not_import_later_files() {
    let dir = TempDir::new().unwrap();
    let data = dir.path().join("new-app-data");
    let db = Database::open(&data).unwrap();
    assert!(db.list().unwrap().is_empty());
    assert_eq!(count(&db.connection, "data_migrations"), 1);
    assert!(!data.join("projects").exists());
    drop(db);
    legacy(&data, "late-file", &project("late"));
    assert!(Database::open(&data).unwrap().list().unwrap().is_empty());
}

#[test]
fn existing_database_without_import_marker_is_imported() {
    let dir = TempDir::new().unwrap();
    let mut connection = raw_database(dir.path());
    migrations::apply(&mut connection).unwrap();
    drop(connection);
    legacy(dir.path(), "one", &project("one"));
    let db = Database::open(dir.path()).unwrap();
    assert_eq!(db.list().unwrap().len(), 1);
    assert_eq!(count(&db.connection, "data_migrations"), 1);
}

#[test]
fn restart_keeps_database_edits_and_never_rereads_legacy_files() {
    let dir = TempDir::new().unwrap();
    let original = legacy(dir.path(), "one", &project("one"));
    let mut db = Database::open(dir.path()).unwrap();
    let mut changed = project("one");
    changed["lastOpened"] = json!("2026-10-20T12:00:00Z");
    changed["name"] = json!("Updated");
    db.save("one", &changed.to_string()).unwrap();
    db.save("two", &project("two").to_string()).unwrap();
    assert_eq!(fs::read(dir.path().join("projects/one")).unwrap(), original);
    assert!(!dir.path().join("projects/two").exists());
    drop(db);
    // A changed or unreadable old snapshot must not break future startup.
    fs::write(dir.path().join("projects/one"), "not JSON anymore").unwrap();
    let db = Database::open(dir.path()).unwrap();
    assert_eq!(db.list().unwrap().len(), 2);
    assert_eq!(
        serde_json::to_value(db.get("one").unwrap()).unwrap(),
        changed
    );
    assert_eq!(count(&db.connection, "data_migrations"), 1);
}

#[test]
fn malformed_file_aborts_import_and_fixed_file_can_be_retried() {
    let dir = TempDir::new().unwrap();
    let original = legacy(dir.path(), "a-good", &project("good"));
    fs::write(dir.path().join("projects/z-bad"), "{broken").unwrap();
    let error = format!("{:#}", Database::open(dir.path()).err().unwrap());
    assert!(error.contains("z-bad"), "{error}");
    let raw = raw_database(dir.path());
    assert_eq!(count(&raw, "projects"), 0);
    assert_eq!(count(&raw, "data_migrations"), 0);
    drop(raw);
    assert_eq!(
        fs::read(dir.path().join("projects/a-good")).unwrap(),
        original
    );
    legacy(dir.path(), "z-bad", &project("fixed"));
    assert_eq!(Database::open(dir.path()).unwrap().list().unwrap().len(), 2);
}

#[test]
fn duplicate_ids_report_both_files_without_importing_anything() {
    let dir = TempDir::new().unwrap();
    legacy(dir.path(), "a.json", &project("same"));
    legacy(dir.path(), "b.json", &project("same"));
    let error = format!("{:#}", Database::open(dir.path()).err().unwrap());
    for part in ["Duplicate project ID", "same", "a.json", "b.json"] {
        assert!(error.contains(part), "{error}");
    }
    let raw = raw_database(dir.path());
    assert_eq!(count(&raw, "projects"), 0);
    assert_eq!(count(&raw, "data_migrations"), 0);
}

#[test]
fn sql_failure_rolls_back_prior_inserts_and_preserves_existing_database() {
    let dir = TempDir::new().unwrap();
    let mut db = Database::open(dir.path()).unwrap();
    db.save("existing", &project("existing").to_string())
        .unwrap();
    db.connection
        .execute("DELETE FROM data_migrations", [])
        .unwrap();
    drop(db);
    legacy(dir.path(), "a-new", &project("new"));
    let mut conflict = project("existing");
    conflict["name"] = json!("Must not overwrite");
    legacy(dir.path(), "z-conflict", &conflict);
    let error = format!("{:#}", Database::open(dir.path()).err().unwrap());
    assert!(error.contains("z-conflict"), "{error}");
    let raw = raw_database(dir.path());
    assert_eq!(count(&raw, "projects"), 1);
    assert_eq!(count(&raw, "data_migrations"), 0);
    let stored_name: String = raw
        .query_row("SELECT name FROM projects", [], |row| row.get(0))
        .unwrap();
    assert_eq!(stored_name, project("existing")["name"].as_str().unwrap());
}

#[test]
fn unreadable_text_is_reported_instead_of_silently_skipped() {
    let dir = TempDir::new().unwrap();
    legacy(dir.path(), "invalid-utf8", &project("one"));
    fs::write(dir.path().join("projects/invalid-utf8"), [0xff, 0xfe]).unwrap();
    let error = format!("{:#}", Database::open(dir.path()).err().unwrap());
    assert!(error.contains("invalid-utf8"), "{error}");
    assert_eq!(count(&raw_database(dir.path()), "data_migrations"), 0);
}

#[test]
fn projects_path_that_is_a_file_is_not_treated_as_fresh_install() {
    let dir = TempDir::new().unwrap();
    fs::write(dir.path().join("projects"), "keep me").unwrap();
    assert!(Database::open(dir.path()).is_err());
    assert_eq!(count(&raw_database(dir.path()), "data_migrations"), 0);
    assert_eq!(
        fs::read_to_string(dir.path().join("projects")).unwrap(),
        "keep me"
    );
}

#[test]
fn invalid_save_cannot_modify_existing_project() {
    let dir = TempDir::new().unwrap();
    let mut db = Database::open(dir.path()).unwrap();
    db.save("one", &project("one").to_string()).unwrap();
    assert!(
        db.save("filename.json", &project("one").to_string())
            .is_err()
    );
    assert!(db.save("one", "not json").is_err());
    assert!(db.save("", &project("").to_string()).is_err());
    let mut malformed = project("one");
    malformed["remote"] = json!(["Ssh", "missing username"]);
    assert!(db.save("one", &malformed.to_string()).is_err());
    assert_eq!(
        serde_json::to_value(db.get("one").unwrap()).unwrap(),
        project("one")
    );
    assert!(
        db.get("missing")
            .unwrap_err()
            .to_string()
            .contains("not found")
    );
}

#[test]
fn failed_schema_migration_rolls_back_ddl_and_version() {
    let mut connection = Connection::open_in_memory().unwrap();
    connection
        .execute("CREATE TABLE data_migrations (unexpected TEXT)", [])
        .unwrap();
    assert!(migrations::apply(&mut connection).is_err());
    let projects_exist: bool = connection
        .query_row(
            "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE name = 'projects')",
            [],
            |row| row.get(0),
        )
        .unwrap();
    assert!(!projects_exist);
    let version: i64 = connection
        .pragma_query_value(None, "user_version", |row| row.get(0))
        .unwrap();
    assert_eq!(version, 0);
}

#[test]
fn newer_schema_is_not_downgraded_or_imported() {
    let dir = TempDir::new().unwrap();
    let raw = raw_database(dir.path());
    raw.pragma_update(None, "user_version", 2).unwrap();
    drop(raw);
    legacy(dir.path(), "one", &project("one"));
    let error = format!("{:#}", Database::open(dir.path()).err().unwrap());
    assert!(
        error.contains("Unsupported project database version 2"),
        "{error}"
    );
    let raw = raw_database(dir.path());
    let version: i64 = raw
        .pragma_query_value(None, "user_version", |row| row.get(0))
        .unwrap();
    assert_eq!(version, 2);
}

#[test]
fn concurrent_startup_imports_exactly_once() {
    let dir = TempDir::new().unwrap();
    legacy(dir.path(), "one", &project("one"));
    let barrier = Arc::new(std::sync::Barrier::new(2));
    let handles: Vec<_> = (0..2)
        .map(|_| {
            let directory = dir.path().to_owned();
            let barrier = Arc::clone(&barrier);
            std::thread::spawn(move || {
                barrier.wait();
                let db = Database::open(&directory).unwrap();
                assert_eq!(db.list().unwrap().len(), 1);
                assert_eq!(count(&db.connection, "data_migrations"), 1);
            })
        })
        .collect();
    for handle in handles {
        handle.join().unwrap();
    }
}

#[tokio::test]
async fn worker_waits_for_import_and_persists_queued_saves() {
    let dir = TempDir::new().unwrap();
    legacy(dir.path(), "one", &project("one"));
    let storage = Storage::start(Ok(dir.path().to_owned()));
    let (listed, saved) = tokio::join!(
        storage.list(),
        storage.save("two".into(), project("two").to_string())
    );
    assert!(
        listed
            .unwrap()
            .iter()
            .any(|json| ProjectData::from_json(json).unwrap().id == "one")
    );
    saved.unwrap();
    assert_eq!(storage.get("two".into()).await.unwrap().id, "two");
    drop(storage);
    assert_eq!(Database::open(dir.path()).unwrap().list().unwrap().len(), 2);
}

#[tokio::test]
async fn worker_reports_initialization_error_and_blocks_writes() {
    let dir = TempDir::new().unwrap();
    legacy(dir.path(), "broken", &project("one"));
    fs::write(dir.path().join("projects/broken"), "broken").unwrap();
    let storage = Storage::start(Ok(dir.path().to_owned()));
    let error = storage.list().await.unwrap_err();
    assert!(error.contains("broken"));
    assert!(error.contains("restart ProjLaunch"));
    assert_eq!(
        storage
            .save("two".into(), project("two").to_string())
            .await
            .unwrap_err(),
        error
    );
    assert_eq!(storage.get("one".into()).await.unwrap_err(), error);
    drop(storage);
    assert_eq!(count(&raw_database(dir.path()), "projects"), 0);
}

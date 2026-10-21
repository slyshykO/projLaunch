use std::{fs, path::Path, sync::Arc};

use rusqlite::Connection;
use serde_json::{Value, json};
use tempfile::TempDir;

use super::{
    Storage,
    database::{Database, SCHEMA_VERSION},
    legacy_import::IMPORT_NAME,
    migrations,
};
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

fn workspace(root: &Path) -> crate::workspace::Workspace {
    serde_json::from_value(json!({
        "version": 1,
        "activeRoot": root,
        "tabs": [{
            "root": root, "name": "Репозиторій", "filesWidth": 32.5, "filesHeight": 60.0,
            "selection": { "commit": null, "parent": null, "file": { "target": "unstaged", "path": "src/main.rs" } }
        }]
    })).unwrap()
}

#[test]
fn workspace_migration_preserves_v1_projects_and_import_marker() {
    let dir = TempDir::new().unwrap();
    let raw = raw_database(dir.path());
    raw.execute_batch(include_str!("../../migrations/0001_projects.sql"))
        .unwrap();
    raw.pragma_update(None, "user_version", 1).unwrap();
    raw.execute(
        "INSERT INTO data_migrations(name, item_count) VALUES (?1, 0)",
        [IMPORT_NAME],
    )
    .unwrap();
    super::database::insert(
        &raw,
        &ProjectData::from_json(&project("one").to_string()).unwrap(),
    )
    .unwrap();
    drop(raw);
    let db = Database::open(dir.path()).unwrap();
    assert!(db.load_workspace().unwrap().is_none());
    assert_eq!(db.get("one").unwrap().name, project("one")["name"]);
    assert_eq!(count(&db.connection, "data_migrations"), 1);
    assert_eq!(
        db.connection
            .pragma_query_value(None, "user_version", |row| row.get::<_, i64>(0))
            .unwrap(),
        SCHEMA_VERSION
    );
}

#[test]
fn workspace_roundtrip_keeps_order_selection_layout_and_empty_session() {
    let dir = TempDir::new().unwrap();
    let mut db = Database::open(dir.path()).unwrap();
    let mut saved = workspace(dir.path());
    let mut other = saved.tabs[0].clone();
    other.root = dir
        .path()
        .join("missing-repository")
        .to_string_lossy()
        .into_owned();
    other.selection = None;
    saved.tabs.push(other);
    db.save_workspace(&saved).unwrap();
    drop(db);
    let mut db = Database::open(dir.path()).unwrap();
    assert_eq!(
        serde_json::to_value(db.load_workspace().unwrap().unwrap()).unwrap(),
        serde_json::to_value(&saved).unwrap()
    );
    saved.tabs.clear();
    saved.active_root = None;
    db.save_workspace(&saved).unwrap();
    drop(db);
    assert!(
        Database::open(dir.path())
            .unwrap()
            .load_workspace()
            .unwrap()
            .unwrap()
            .tabs
            .is_empty()
    );
}

#[test]
fn invalid_workspace_never_overwrites_the_last_saved_session() {
    let dir = TempDir::new().unwrap();
    let mut db = Database::open(dir.path()).unwrap();
    let saved = workspace(dir.path());
    db.save_workspace(&saved).unwrap();
    for invalid in [
        {
            let mut v = saved.clone();
            v.version = 2;
            v
        },
        {
            let mut v = saved.clone();
            v.tabs[0].files_width = f64::NAN;
            v
        },
        {
            let mut v = saved.clone();
            v.tabs[0].files_height = 99.0;
            v
        },
        {
            let mut v = saved.clone();
            v.tabs.push(v.tabs[0].clone());
            v
        },
        {
            let mut v = saved.clone();
            v.active_root = Some("missing".into());
            v
        },
        {
            let mut v = saved.clone();
            v.tabs[0]
                .selection
                .as_mut()
                .unwrap()
                .file
                .as_mut()
                .unwrap()
                .target = "commit".into();
            v
        },
    ] {
        assert!(db.save_workspace(&invalid).is_err());
    }
    assert_eq!(
        serde_json::to_value(db.load_workspace().unwrap().unwrap()).unwrap(),
        serde_json::to_value(saved).unwrap()
    );
}

#[test]
fn corrupt_workspace_is_reported_without_blocking_project_storage() {
    let dir = TempDir::new().unwrap();
    let mut db = Database::open(dir.path()).unwrap();
    db.connection
        .execute(
            "INSERT INTO workspace_state(id, content) VALUES (1, 'invalid json')",
            [],
        )
        .unwrap();
    assert!(db.load_workspace().is_err());
    db.save("one", &project("one").to_string()).unwrap();
    assert_eq!(db.list().unwrap().len(), 1);
    assert_eq!(
        db.connection
            .query_row("SELECT content FROM workspace_state", [], |row| row
                .get::<_, String>(0))
            .unwrap(),
        "invalid json"
    );
}

#[tokio::test]
async fn workspace_worker_acknowledges_durable_writes() {
    let dir = TempDir::new().unwrap();
    let storage = Storage::start(Ok(dir.path().to_owned()));
    assert!(storage.load_workspace().await.unwrap().is_none());
    let saved = workspace(dir.path());
    storage.save_workspace(saved.clone()).await.unwrap();
    assert_eq!(
        serde_json::to_value(storage.load_workspace().await.unwrap().unwrap()).unwrap(),
        serde_json::to_value(saved).unwrap()
    );
    drop(storage);
    assert!(
        Database::open(dir.path())
            .unwrap()
            .load_workspace()
            .unwrap()
            .is_some()
    );
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
    let newer_version = SCHEMA_VERSION + 1;
    raw.pragma_update(None, "user_version", newer_version)
        .unwrap();
    drop(raw);
    legacy(dir.path(), "one", &project("one"));
    let error = format!("{:#}", Database::open(dir.path()).err().unwrap());
    assert!(
        error.contains(&format!(
            "Unsupported project database version {newer_version}"
        )),
        "{error}"
    );
    let raw = raw_database(dir.path());
    let version: i64 = raw
        .pragma_query_value(None, "user_version", |row| row.get(0))
        .unwrap();
    assert_eq!(version, newer_version);
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

#[test]
fn second_instance_reads_live_updates_but_cannot_write_or_release_owner() {
    let dir = TempDir::new().unwrap();
    let mut writer = Database::open(dir.path()).unwrap();
    assert!(writer.access().can_write);
    writer.save("one", &project("one").to_string()).unwrap();
    let mut reader = Database::open(dir.path()).unwrap();
    assert!(!reader.access().can_write);
    assert_eq!(reader.access().owner_pid, Some(std::process::id()));
    assert_eq!(reader.list().unwrap().len(), 1);
    assert!(
        reader
            .save("two", &project("two").to_string())
            .unwrap_err()
            .to_string()
            .contains("read-only")
    );
    assert!(reader.save_workspace(&workspace(dir.path())).is_err());
    assert!(
        reader
            .connection
            .execute("DELETE FROM projects", [])
            .is_err(),
        "SQLite itself enforces read-only access"
    );
    writer.save("two", &project("two").to_string()).unwrap();
    assert_eq!(reader.list().unwrap().len(), 2);
    reader.release().unwrap();
    drop(reader);
    assert_eq!(count(&writer.connection, "database_owner"), 1);
    writer.save_workspace(&workspace(dir.path())).unwrap();
    drop(writer);
    assert_eq!(count(&raw_database(dir.path()), "database_owner"), 0);
    assert!(Database::open(dir.path()).unwrap().access().can_write);
}

#[test]
fn ownership_migration_preserves_v2_projects_and_workspace() {
    let dir = TempDir::new().unwrap();
    let raw = raw_database(dir.path());
    raw.execute_batch(include_str!("../../migrations/0001_projects.sql"))
        .unwrap();
    raw.execute_batch(include_str!("../../migrations/0002_workspace.sql"))
        .unwrap();
    raw.pragma_update(None, "user_version", 2).unwrap();
    raw.execute(
        "INSERT INTO data_migrations(name, item_count) VALUES (?1, 0)",
        [IMPORT_NAME],
    )
    .unwrap();
    super::database::insert(
        &raw,
        &ProjectData::from_json(&project("one").to_string()).unwrap(),
    )
    .unwrap();
    let saved = workspace(dir.path());
    raw.execute(
        "INSERT INTO workspace_state VALUES (1, ?1)",
        [serde_json::to_string(&saved).unwrap()],
    )
    .unwrap();
    let db = Database::open(dir.path()).unwrap();
    assert!(db.access().can_write);
    assert_eq!(db.get("one").unwrap().name, project("one")["name"]);
    assert_eq!(
        serde_json::to_value(db.load_workspace().unwrap().unwrap()).unwrap(),
        serde_json::to_value(saved).unwrap()
    );
    assert_eq!(count(&db.connection, "data_migrations"), 1);
    assert_eq!(
        db.connection
            .pragma_query_value(None, "user_version", |row| row.get::<_, i64>(0))
            .unwrap(),
        SCHEMA_VERSION
    );
}

#[test]
fn startup_recovers_if_first_owner_dies_before_creating_database() {
    let dir = TempDir::new().unwrap();
    let lock = super::ownership::try_lock(dir.path()).unwrap().unwrap();
    let directory = dir.path().to_owned();
    let waiter = std::thread::spawn(move || Database::open(&directory).unwrap().access().can_write);
    std::thread::sleep(std::time::Duration::from_millis(100));
    drop(lock);
    assert!(waiter.join().unwrap());
}

#[test]
fn reader_stays_read_only_until_restarted_after_owner_exits() {
    let dir = TempDir::new().unwrap();
    let writer = Database::open(dir.path()).unwrap();
    let mut reader = Database::open(dir.path()).unwrap();
    drop(writer);
    assert!(!reader.access().can_write);
    assert!(reader.save("one", &project("one").to_string()).is_err());
    let mut next_writer = Database::open(dir.path()).unwrap();
    next_writer
        .save("one", &project("one").to_string())
        .unwrap();
    assert_eq!(reader.list().unwrap().len(), 1);
}

#[test]
fn simultaneous_startup_has_exactly_one_writer() {
    let dir = TempDir::new().unwrap();
    legacy(dir.path(), "one", &project("one"));
    let start = Arc::new(std::sync::Barrier::new(3));
    let (send, recv) = std::sync::mpsc::channel();
    let mut releases = Vec::new();
    let mut handles = Vec::new();
    for _ in 0..2 {
        let directory = dir.path().to_owned();
        let start = Arc::clone(&start);
        let send = send.clone();
        let (release, wait) = std::sync::mpsc::channel::<()>();
        releases.push(release);
        handles.push(std::thread::spawn(move || {
            start.wait();
            let db = Database::open(&directory).unwrap();
            assert_eq!(db.list().unwrap().len(), 1);
            send.send(db.access().can_write).unwrap();
            let _ = wait.recv();
        }));
    }
    start.wait();
    let modes = (0..2)
        .map(|_| {
            recv.recv_timeout(std::time::Duration::from_secs(15))
                .unwrap()
        })
        .collect::<Vec<_>>();
    assert_eq!(modes.iter().filter(|&&writable| writable).count(), 1);
    drop(releases);
    for handle in handles {
        handle.join().unwrap();
    }
    assert_eq!(count(&raw_database(dir.path()), "database_owner"), 0);
}

#[test]
fn stale_pid_and_reused_live_pid_do_not_block_a_new_writer() {
    let dir = TempDir::new().unwrap();
    drop(Database::open(dir.path()).unwrap());
    let raw = raw_database(dir.path());
    // Include this still-live process to model PID reuse. OS ownership, not
    // a process name alone, decides whether a stale row can be replaced.
    for pid in [u32::MAX, std::process::id()] {
        raw.execute(
            "INSERT INTO database_owner VALUES (1, ?1, 0, 'old/projlaunch.exe', 'stale')",
            [pid],
        )
        .unwrap();
        let mut db = Database::open(dir.path()).unwrap();
        assert!(db.access().can_write);
        db.save("one", &project("one").to_string()).unwrap();
        assert_eq!(db.access().owner_pid, Some(std::process::id()));
        drop(db);
        assert_eq!(count(&raw, "database_owner"), 0);
    }
}

#[test]
fn owner_token_prevents_writes_and_cleanup_of_another_lease() {
    let dir = TempDir::new().unwrap();
    let mut db = Database::open(dir.path()).unwrap();
    let raw = raw_database(dir.path());
    raw.execute(
        "UPDATE database_owner SET instance_token = 'replacement'",
        [],
    )
    .unwrap();
    assert!(db.save("one", &project("one").to_string()).is_err());
    assert!(db.save_workspace(&workspace(dir.path())).is_err());
    drop(db);
    let token: String = raw
        .query_row("SELECT instance_token FROM database_owner", [], |row| {
            row.get(0)
        })
        .unwrap();
    assert_eq!(token, "replacement");
    assert!(Database::open(dir.path()).unwrap().access().can_write);
}

#[tokio::test]
async fn explicit_shutdown_releases_ownership_even_while_storage_handles_exist() {
    let dir = TempDir::new().unwrap();
    let storage = Storage::start(Ok(dir.path().to_owned()));
    let retained = storage.clone();
    assert!(storage.access().await.unwrap().can_write);
    storage
        .save("one".into(), project("one").to_string())
        .await
        .unwrap();
    storage.shutdown().await.unwrap();
    assert_eq!(count(&raw_database(dir.path()), "database_owner"), 0);
    assert!(
        retained
            .save("two".into(), project("two").to_string())
            .await
            .is_err()
    );
    let db = Database::open(dir.path()).unwrap();
    assert!(db.access().can_write);
    assert_eq!(db.list().unwrap().len(), 1);
}

// Run only as a subprocess of the crash test below, never against user data.
#[test]
#[ignore]
fn ownership_child_process() {
    let directory = std::env::var_os("PROJLAUNCH_TEST_OWNER_DIR").expect("child test directory");
    let directory = Path::new(&directory);
    let mut db = Database::open(directory).unwrap();
    assert!(db.access().can_write);
    db.save("one", &project("one").to_string()).unwrap();
    fs::write(directory.join("child-ready"), "ready").unwrap();
    loop {
        std::thread::sleep(std::time::Duration::from_millis(100));
    }
}

#[test]
fn killed_owner_is_recovered_on_next_start_without_losing_projects() {
    struct Child(std::process::Child);
    impl Drop for Child {
        fn drop(&mut self) {
            let _ = self.0.kill();
            let _ = self.0.wait();
        }
    }
    let dir = TempDir::new().unwrap();
    let mut command = std::process::Command::new(std::env::current_exe().unwrap());
    command
        .args([
            "--ignored",
            "--exact",
            "storage::tests::ownership_child_process",
            "--nocapture",
        ])
        .env("PROJLAUNCH_TEST_OWNER_DIR", dir.path())
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null());
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x08000000);
    }
    let mut child = Child(command.spawn().unwrap());
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(15);
    while !dir.path().join("child-ready").exists() {
        assert!(
            child.0.try_wait().unwrap().is_none(),
            "Child failed before opening storage"
        );
        assert!(
            std::time::Instant::now() < deadline,
            "Child did not initialize storage"
        );
        std::thread::sleep(std::time::Duration::from_millis(25));
    }
    let mut reader = Database::open(dir.path()).unwrap();
    assert!(!reader.access().can_write);
    assert_eq!(reader.access().owner_pid, Some(child.0.id()));
    assert_eq!(reader.list().unwrap().len(), 1);
    assert!(reader.save("two", &project("two").to_string()).is_err());
    child.0.kill().unwrap();
    child.0.wait().unwrap();
    assert_eq!(
        count(&reader.connection, "database_owner"),
        1,
        "Force kill skips cleanup"
    );
    let mut recovered = Database::open(dir.path()).unwrap();
    assert!(recovered.access().can_write);
    assert_eq!(recovered.list().unwrap().len(), 1);
    recovered.save("two", &project("two").to_string()).unwrap();
    assert_eq!(reader.list().unwrap().len(), 2);
    drop(recovered);
    assert_eq!(count(&reader.connection, "database_owner"), 0);
}

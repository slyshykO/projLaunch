use std::{
    fs::File,
    path::Path,
    time::{Duration, Instant},
};

use super::ownership::{self, Access};
use anyhow::{Context, Result, ensure};
use rusqlite::{Connection, OpenFlags, OptionalExtension, Row, params};

use crate::project::ProjectData;
use crate::workspace::Workspace;

pub const SCHEMA_VERSION: i64 = 3;

const COLUMNS: &str =
    "id, name, last_opened, description, path, ide, environment_json, remote_json";
const INSERT: &str = "INSERT INTO projects
    (id, name, last_opened, description, path, ide, environment_json, remote_json)
    VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)";

pub(super) struct Database {
    pub(super) connection: Connection,
    access: Access,
    owner_token: Option<String>,
    // Keep the lock until after the SQLite connection is closed.
    _writer_lock: Option<File>,
}

impl Database {
    pub(super) fn load_workspace(&self) -> Result<Option<Workspace>> {
        let content: Option<String> = self
            .connection
            .query_row(
                "SELECT content FROM workspace_state WHERE id = 1",
                [],
                |row| row.get(0),
            )
            .optional()?;
        content.as_deref().map(Workspace::from_json).transpose()
    }

    pub(super) fn save_workspace(&mut self, workspace: &Workspace) -> Result<()> {
        self.ensure_writable()?;
        workspace.validate()?;
        let content = serde_json::to_string(workspace)?;
        ensure!(
            content.len() <= 1024 * 1024,
            "Workspace state exceeds 1 MiB"
        );
        self.connection.execute(
            "INSERT INTO workspace_state (id, content) VALUES (1, ?1) ON CONFLICT(id) DO UPDATE SET content = excluded.content",
            [content],
        )?;
        Ok(())
    }

    pub(super) fn open(app_data_dir: &Path) -> Result<Self> {
        std::fs::create_dir_all(app_data_dir)
            .with_context(|| format!("Cannot create {}", app_data_dir.display()))?;
        let path = app_data_dir.join("projlaunch.db");
        let mut writer_lock = ownership::try_lock(app_data_dir)?;
        let deadline = Instant::now() + Duration::from_secs(5);
        while writer_lock.is_none() {
            // Another process may still be creating/migrating its database.
            // Open strictly read-only; never migrate or import in a reader.
            let read = (|| -> Result<Self> {
                let connection =
                    Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY)?;
                connection.busy_timeout(Duration::from_secs(5))?;
                let version: i64 =
                    connection.pragma_query_value(None, "user_version", |row| row.get(0))?;
                ensure!(
                    version == SCHEMA_VERSION,
                    "Database owner is initializing the database, or its schema is incompatible (version {version})"
                );
                let owner_pid = ownership::owner_pid(&connection)?;
                ensure!(
                    owner_pid.is_some(),
                    "Database owner is still initializing storage"
                );
                Ok(Self {
                    connection,
                    access: Access {
                        can_write: false,
                        owner_pid,
                    },
                    owner_token: None,
                    _writer_lock: None,
                })
            })();
            if read.is_ok() {
                return read;
            }
            writer_lock = ownership::try_lock(app_data_dir)?;
            if writer_lock.is_none() {
                if Instant::now() >= deadline {
                    return read;
                }
                std::thread::sleep(Duration::from_millis(25));
            }
        }
        let mut connection =
            Connection::open(&path).with_context(|| format!("Cannot open {}", path.display()))?;
        connection.busy_timeout(Duration::from_secs(5))?;
        connection.pragma_update(None, "foreign_keys", true)?;
        super::migrations::apply(&mut connection)?;
        super::legacy_import::run(&mut connection, &app_data_dir.join("projects"))?;
        let token = ownership::claim(&mut connection)?;
        Ok(Self {
            connection,
            access: Access {
                can_write: true,
                owner_pid: Some(std::process::id()),
            },
            owner_token: Some(token),
            _writer_lock: writer_lock,
        })
    }

    pub(super) fn access(&self) -> Access {
        self.access.clone()
    }

    fn ensure_writable(&self) -> Result<()> {
        ensure!(
            self.access.can_write,
            "Database is read-only: another ProjLaunch instance owns write access. Restart this instance after the owner exits to enable changes."
        );
        let owns: bool = self.connection.query_row(
            "SELECT EXISTS(SELECT 1 FROM database_owner WHERE id = 1 AND instance_token = ?1)",
            [&self.owner_token],
            |row| row.get(0),
        )?;
        ensure!(
            owns,
            "Database write ownership was lost; restart ProjLaunch"
        );
        Ok(())
    }

    pub(super) fn release(&mut self) -> Result<()> {
        if let Some(token) = &self.owner_token {
            self.connection.execute(
                "DELETE FROM database_owner WHERE id = 1 AND instance_token = ?1",
                [token],
            )?;
            self.owner_token = None;
            self.access.can_write = false;
        }
        Ok(())
    }

    pub(super) fn list(&self) -> Result<Vec<String>> {
        let mut statement = self
            .connection
            .prepare(&format!("SELECT {COLUMNS} FROM projects ORDER BY id"))?;
        statement
            .query_map([], read_project)?
            .map(|project| Ok(serde_json::to_string(&project?)?))
            .collect()
    }

    pub(super) fn get(&self, id: &str) -> Result<ProjectData> {
        self.connection
            .query_row(
                &format!("SELECT {COLUMNS} FROM projects WHERE id = ?1"),
                [id],
                read_project,
            )
            .optional()?
            .with_context(|| format!("Project `{id}` not found"))
    }

    pub(super) fn save(&mut self, id: &str, content: &str) -> Result<()> {
        self.ensure_writable()?;
        let project = ProjectData::from_json(content)?;
        ensure!(
            id == project.id,
            "Project ID does not match the saved record"
        );
        let sql = format!(
            "{INSERT} ON CONFLICT(id) DO UPDATE SET
             name = excluded.name, last_opened = excluded.last_opened,
             description = excluded.description, path = excluded.path,
             ide = excluded.ide, environment_json = excluded.environment_json,
             remote_json = excluded.remote_json"
        );
        write_project(&self.connection, &sql, &project)
    }
}

impl Drop for Database {
    fn drop(&mut self) {
        if let Err(error) = self.release() {
            tracing::error!(%error, "Could not clear database ownership; next writer will recover the record");
        }
    }
}

pub(super) fn insert(connection: &Connection, project: &ProjectData) -> Result<()> {
    // Import must fail on conflicts, never replace existing records.
    write_project(connection, INSERT, project)
}

fn write_project(connection: &Connection, sql: &str, project: &ProjectData) -> Result<()> {
    connection.execute(
        sql,
        params![
            project.id,
            project.name,
            project.last_opened,
            project.description,
            project.path,
            project.ide,
            serde_json::to_string(&project.environment)?,
            serde_json::to_string(&project.remote)?,
        ],
    )?;
    Ok(())
}

fn read_project(row: &Row<'_>) -> rusqlite::Result<ProjectData> {
    fn json_column<T: serde::de::DeserializeOwned>(
        row: &Row<'_>,
        index: usize,
    ) -> rusqlite::Result<T> {
        let json: String = row.get(index)?;
        serde_json::from_str(&json).map_err(|error| {
            rusqlite::Error::FromSqlConversionFailure(
                index,
                rusqlite::types::Type::Text,
                Box::new(error),
            )
        })
    }

    Ok(ProjectData {
        id: row.get(0)?,
        name: row.get(1)?,
        last_opened: row.get(2)?,
        description: row.get(3)?,
        path: row.get(4)?,
        ide: row.get(5)?,
        environment: json_column(row, 6)?,
        remote: json_column(row, 7)?,
    })
}

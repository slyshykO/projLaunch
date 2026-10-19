use std::{collections::HashMap, io::ErrorKind, path::Path};

use anyhow::{Context, Result, bail};
use rusqlite::{Connection, TransactionBehavior, params};

use crate::project::ProjectData;

pub(super) const IMPORT_NAME: &str = "legacy_projects_v1";

pub(super) fn run(connection: &mut Connection, directory: &Path) -> Result<()> {
    let tx = connection.transaction_with_behavior(TransactionBehavior::Immediate)?;
    let completed: bool = tx.query_row(
        "SELECT EXISTS(SELECT 1 FROM data_migrations WHERE name = ?1)",
        [IMPORT_NAME],
        |row| row.get(0),
    )?;
    if completed {
        tx.commit()?;
        return Ok(());
    }

    let entries = match std::fs::read_dir(directory) {
        Ok(entries) => Some(entries),
        Err(error) if error.kind() == ErrorKind::NotFound => None,
        Err(error) => {
            return Err(error).with_context(|| {
                format!("Cannot read legacy projects in {}", directory.display())
            });
        }
    };
    let mut paths = Vec::new();
    if let Some(entries) = entries {
        for entry in entries {
            let path = entry
                .with_context(|| format!("Cannot list {}", directory.display()))?
                .path();
            let metadata = std::fs::metadata(&path)
                .with_context(|| format!("Cannot inspect {}", path.display()))?;
            if metadata.is_file() {
                // The original app also used extensionless files named after IDs.
                paths.push(path);
            }
        }
    }
    paths.sort();

    let mut projects = Vec::new();
    let mut ids = HashMap::new();
    for path in paths {
        let content = std::fs::read_to_string(&path)
            .with_context(|| format!("Cannot read legacy project {}", path.display()))?;
        let project = ProjectData::from_json(&content)
            .with_context(|| format!("Cannot import legacy project {}", path.display()))?;
        if let Some(previous) = ids.insert(project.id.clone(), path.clone()) {
            bail!(
                "Duplicate project ID `{}` in {} and {}",
                project.id,
                previous.display(),
                path.display()
            );
        }
        projects.push((path, project));
    }

    for (path, project) in &projects {
        super::database::insert(&tx, project).with_context(|| {
            format!(
                "Cannot import project `{}` from {}",
                project.id,
                path.display()
            )
        })?;
    }
    tx.execute(
        "INSERT INTO data_migrations (name, item_count) VALUES (?1, ?2)",
        params![IMPORT_NAME, projects.len() as i64],
    )?;
    tx.commit().context("Cannot commit legacy project import")?;
    // Source files are deliberately never written, renamed or removed.
    Ok(())
}

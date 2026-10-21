use anyhow::{Context, Result, ensure};
use rusqlite::{Connection, TransactionBehavior};

use super::database::SCHEMA_VERSION;

pub(super) fn apply(connection: &mut Connection) -> Result<()> {
    // The write lock also serializes startup of multiple app instances.
    let tx = connection.transaction_with_behavior(TransactionBehavior::Immediate)?;
    let version: i64 = tx.pragma_query_value(None, "user_version", |row| row.get(0))?;
    ensure!(
        (0..=SCHEMA_VERSION).contains(&version),
        "Unsupported project database version {version}; use a compatible ProjLaunch version"
    );
    if version == 0 {
        tx.execute_batch(include_str!("../../migrations/0001_projects.sql"))
            .context("Cannot create project database schema")?;
        tx.pragma_update(None, "user_version", 1)?;
    }
    if version < 2 {
        tx.execute_batch(include_str!("../../migrations/0002_workspace.sql"))
            .context("Cannot create workspace state schema")?;
        tx.pragma_update(None, "user_version", 2)?;
    }
    if version < 3 {
        tx.execute_batch(include_str!("../../migrations/0003_database_owner.sql"))
            .context("Cannot create database ownership schema")?;
    }
    tx.pragma_update(None, "user_version", SCHEMA_VERSION)?;
    tx.commit()?;
    Ok(())
}
